namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;

    /// <summary>
    /// Plans WoT forms for every protocol driver loaded into
    /// <see cref="ProtocolDriverRegistry"/>, so the WoT.con registry can materialize
    /// Thing Descriptions whose forms target our drivers.
    /// </summary>
    /// <remarks>
    /// A single binder serves all drivers because Edge Translator TDs use hrefs that
    /// are relative to the TD <c>base</c>, and <see cref="Match"/> only sees the form.
    /// A relative form is therefore claimed at low priority and resolved against
    /// <see cref="WotBindingPlanContext.BaseUri"/> in <see cref="Compile"/>, where the
    /// driver is picked by scheme. The raw form is carried to the executor in the
    /// addressing metadata, because drivers parse their own form vocabulary through
    /// <see cref="IProtocolDriver.CreateTag"/>.
    /// </remarks>
    public sealed class DriverWotBinder : WotProtocolBinderBase
    {
        public const string BindingId = "edgetranslator.driver";

        public const string BindingUri = "https://opcfoundation.org/UA-EdgeTranslator/driver";

        internal const string FormMetadataKey = "et:form";

        private static readonly WotBindingIdentity s_identity =
            new(BindingId, "1.0", BindingUri, "UA Edge Translator protocol drivers");

        private static readonly WotBindingCapability s_capability = new(
            BindingUri,
            "UA Edge Translator protocol drivers",
            new WotBindingSource(BindingUri, "1.0", WotBindingMaturity.UnofficialDraft,
                note: "Bridges WoT forms to the protocol drivers loaded by UA Edge Translator."),
            [
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty,
                WoTBindingCapabilityEnum.InvokeAction,
                WoTBindingCapabilityEnum.SubscribeEvent,
                WoTBindingCapabilityEnum.UnsubscribeEvent
            ],
            ["application/json", "text/plain"],
            isExecutable: true);

        private readonly ProtocolDriverRegistry _drivers;

        public DriverWotBinder(ProtocolDriverRegistry drivers)
        {
            _drivers = drivers ?? throw new ArgumentNullException(nameof(drivers));
        }

        public static WotBindingIdentity BinderIdentity => s_identity;

        public override WotBindingIdentity Identity => s_identity;

        public override WotBindingCapability Capability => s_capability;

        protected override IReadOnlyCollection<string> Schemes =>
            _drivers.AllDrivers.Select(driver => driver.Scheme).ToArray();

        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
        {
            ArgumentNullException.ThrowIfNull(form);
            ArgumentNullException.ThrowIfNull(context);

            WotBindingMatch match = MatchStandard(form, context, null);
            if (match.IsMatch)
            {
                return match;
            }

            // A relative href (no scheme) can only be resolved against the TD base,
            // which Compile sees. Claim it below any scheme-based match.
            if (!string.IsNullOrEmpty(form.Href) && SchemeOf(form.Href) is null)
            {
                return WotBindingMatch.Match(WotBindingMatchKind.Scheme, 1);
            }

            return WotBindingMatch.NoMatch;
        }

        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            ArgumentNullException.ThrowIfNull(form);
            ArgumentNullException.ThrowIfNull(context);

            var diagnostics = new List<WotBindingDiagnostic>();
            if (!RequireHref(form, context, diagnostics, out string href))
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            string baseUri = SchemeOf(href) is null ? context.BaseUri : href;
            if (string.IsNullOrEmpty(baseUri)
                || !TryParseUri(baseUri, out Uri endpointUri)
                || !_drivers.TryGetByUri(baseUri, out IProtocolDriver driver))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.UnsupportedScheme,
                    "No loaded protocol driver handles the form's endpoint.",
                    form.Pointer("href")));
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            if (!ResolveCodec(form, context, diagnostics, out WotPayloadDescriptor payload))
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            WotEndpointDescriptor endpoint = new(driver.Scheme, endpointUri.Host, endpointUri.Port, baseUri);

            WotAddressingDescriptor addressing = new(
                href,
                ImmutableDictionary<string, string>.Empty.Add(FormMetadataKey, form.FormElement.GetRawText()));

            ImmutableArray<WotCompiledForm>.Builder entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                WotOperationDescriptor operation = new(capability, op, capability.ToString());
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload,
                    [], Capability.IsExecutable));
            }

            return entries.Count == 0
                ? WotBindingCompilation.Unsupported([.. diagnostics])
                : WotBindingCompilation.Supported(entries.ToImmutable(), [.. diagnostics]);
        }
    }
}
