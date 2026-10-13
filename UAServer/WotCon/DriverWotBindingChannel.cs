namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One planned form bound to a connected <see cref="IAsset"/>. Translates
    /// between the OPC UA value model and the CLR values drivers exchange, and
    /// enforces provisioning mode exactly as the WoT v1.02 node manager does.
    /// </summary>
    internal sealed class DriverWotBindingChannel : IWotBindingChannel
    {
        private const string ProvisioningMessage = "Access to UA Edge Translator is limited while in provisioning mode!";

        private readonly IAsset _asset;

        private readonly AssetTag _tag;

        private readonly string _assetName;

        private readonly Func<bool> _isAssetAccessBlocked;

        private Func<ValueTask> _release;

        /// <param name="release">
        /// Returns this channel's reference to the shared asset connection; called once,
        /// when the channel is disposed.
        /// </param>
        public DriverWotBindingChannel(WotCompiledForm form, IAsset asset, AssetTag tag, string assetName, Func<bool> isAssetAccessBlocked, Func<ValueTask> release = null)
        {
            Form = form;
            _asset = asset;
            _release = release;
            _tag = tag;
            _assetName = assetName;
            _isAssetAccessBlocked = isAssetAccessBlocked ?? throw new ArgumentNullException(nameof(isAssetAccessBlocked));
        }

        public WotCompiledForm Form { get; }

        public async ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_isAssetAccessBlocked())
            {
                return new WotReadResult(StatusCodes.BadNotReadable, DataValue.FromStatusCode(StatusCodes.BadNotReadable), ProvisioningMessage);
            }

            try
            {
                Program.Telemetry?.TagReads.Add(1);
                object value = await _asset.ReadAsync(_tag, cancellationToken).ConfigureAwait(false);
                if (!_asset.IsConnected)
                {
                    Program.Telemetry?.TagReadErrors.Add(1);
                    return new WotReadResult(StatusCodes.BadDataUnavailable, DataValue.FromStatusCode(StatusCodes.BadDataUnavailable));
                }

                return new WotReadResult(StatusCodes.Good, new DataValue(ToVariant(value), StatusCodes.Good, DateTime.UtcNow));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Program.Telemetry?.TagReadErrors.Add(1);
                Log.Logger.Error(ex, "Failed to read tag: {TagName}, Asset: {AssetId}", _tag?.Name, _assetName);
                return new WotReadResult(StatusCodes.BadDataUnavailable, DataValue.FromStatusCode(StatusCodes.BadDataUnavailable));
            }
        }

        public async ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_isAssetAccessBlocked())
            {
                return new WotWriteResult(StatusCodes.BadNotWritable, ProvisioningMessage);
            }

            try
            {
                Program.Telemetry?.TagWrites.Add(1);
                await _asset.WriteAsync(_tag, VariantConversion.ToClrValue(value.WrappedValue), cancellationToken).ConfigureAwait(false);
                return new WotWriteResult(_asset.IsConnected ? StatusCodes.Good : StatusCodes.BadDataUnavailable);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Program.Telemetry?.TagWriteErrors.Add(1);
                Log.Logger.Error(ex, "Failed to write value for tag: {TagName}", _tag?.Name);
                return new WotWriteResult(StatusCodes.BadDataUnavailable);
            }
        }

        public async ValueTask<WotInvokeResult> InvokeAsync(IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            if (_isAssetAccessBlocked())
            {
                return new WotInvokeResult(StatusCodes.BadUserAccessDenied, null, ProvisioningMessage);
            }

            try
            {
                List<object> driverInputs = new(inputs?.Count ?? 0);
                if (inputs != null)
                {
                    foreach (Variant input in inputs)
                    {
                        driverInputs.Add(VariantConversion.ToClrValue(input));
                    }
                }

                // Drivers identify the action by the method's browse name and the
                // asset by its parent, exactly as the WoT v1.02 node manager presented it.
                BaseObjectState parent = new(null) { BrowseName = new QualifiedName(_assetName) };
                MethodState method = new(parent) { BrowseName = new QualifiedName(Form.AffordanceName) };

                // The v1.02 node manager hands drivers the action's forms on Handle; drivers
                // such as Matter read their action addressing (cluster, timed command) there.
                method.Handle = ActionForms();

                AssetActionResult result = await _asset.ExecuteActionAsync(method, driverInputs, cancellationToken).ConfigureAwait(false);

                List<DataValue> outputs = new();
                if (result?.Outputs != null)
                {
                    foreach (object output in result.Outputs)
                    {
                        outputs.Add(new DataValue(ToVariant(output)));
                    }
                }

                string status = result?.Status;
                if (status == null)
                {
                    return new WotInvokeResult(StatusCodes.Uncertain, outputs, "no result");
                }

                // Drivers that report success with an arbitrary message say so explicitly;
                // otherwise only "ok"/"success" count as success, as in the v1.02 manager.
                return result.IsSuccess
                    ? new WotInvokeResult(StatusCodes.Good, outputs)
                    : new WotInvokeResult(StatusCodes.Bad, outputs, status);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Logger.Error(ex, "Failed to execute action on asset: {AssetId}", _assetName);
                return new WotInvokeResult(StatusCodes.BadInternalError);
            }
        }

        /// <summary>
        /// The action's form in the shape the v1.02 node manager put on <c>MethodState.Handle</c>.
        /// </summary>
        private List<GenericForm> ActionForms()
        {
            if (Form?.Addressing?.Metadata == null
                || !Form.Addressing.Metadata.TryGetValue(DriverWotBinder.FormMetadataKey, out string raw)
                || string.IsNullOrEmpty(raw))
            {
                return new List<GenericForm>();
            }

            GenericForm form = Newtonsoft.Json.JsonConvert.DeserializeObject<GenericForm>(raw);
            return form == null ? new List<GenericForm>() : new List<GenericForm> { form };
        }

        public ValueTask<IWotSubscription> ObserveAsync(Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(onNotification);

            int pollingInterval = _tag?.PollingInterval ?? 0;
            TimeSpan interval = TimeSpan.FromMilliseconds(pollingInterval <= 0 ? 1000 : pollingInterval);
            IWotSubscription subscription = new PollingWotSubscription(Form, async token =>
            {
                WotReadResult result = await ReadAsync(token).ConfigureAwait(false);
                onNotification(new WotNotification(result.Value));
                return result.Success;
            }, interval);

            return new ValueTask<IWotSubscription>(subscription);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(onEvent);

            // Only drivers that push events can serve subscribeevent. Reporting the
            // missing capability as a failure (rather than an empty stream) lets the
            // registry raise a WoTBindingFailureEvent, as WoT-Con 1.1 requires for an
            // operation the binding does not support.
            if (_asset is not IEventingAsset eventingAsset)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    $"The protocol driver for asset '{_assetName}' does not support subscribeevent.");
            }

            if (_isAssetAccessBlocked())
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, ProvisioningMessage);
            }

            return new ValueTask<IWotSubscription>(new DriverEventSubscription(Form, eventingAsset, onEvent, _assetName));
        }

        public ValueTask DisposeAsync()
        {
            // The asset is shared across channels; release this channel's reference so
            // the executor disconnects it once no channel uses it.
            Func<ValueTask> release = Interlocked.Exchange(ref _release, null);
            return release?.Invoke() ?? default;
        }

        private static Variant ToVariant(object value)
        {
            if (value is TimeSpan ts)
            {
                value = ts.TotalMilliseconds;
            }
            else if (value is DateTimeOffset dto)
            {
                value = dto.UtcDateTime;
            }

            // Runtime-typed southbound value: see the note in NodeFactory.CreateVariable.
#pragma warning disable CS0618 // Type or member is obsolete
            return new Variant(value, TypeInfo.Construct(value));
#pragma warning restore CS0618
        }
    }
}
