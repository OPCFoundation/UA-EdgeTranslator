// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Collections.Generic;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;

    /// <summary>
    /// Primitive that keep tracks of changes.
    /// </summary>
    /// <typeparam name="T">The underlying type that we keep track of. Must implement <see cref="IEqualityComparer{T}"/>.</typeparam>
    public sealed class ChangeTrackingProperty<T> : IChangeTrackingProperty
    {
        private T current;
        private T original;

        public ChangeTrackingProperty(string propertyName)
        {
            PropertyName = propertyName;
        }

        public ChangeTrackingProperty(string propertyName, T value)
        {
            PropertyName = propertyName;
            this.current = this.original = value;
        }

        /// <summary>
        /// Gets the property name.
        /// </summary>
        public string PropertyName { get; }

        /// <summary>
        /// Gets the value that must be persisted in twin collection.
        /// </summary>
        object IChangeTrackingProperty.Value => this.current;

        /// <summary>
        /// Gets the current value.
        /// </summary>
        public T Get() => this.current;

        /// <summary>
        /// Sets the current value.
        /// </summary>
        public void Set(T value) => this.current = value;

        /// <summary>
        /// Gets if the value has changed.
        /// </summary>
        public bool IsDirty() => !EqualityComparer<T>.Default.Equals(this.current, this.original);

        /// <summary>
        /// Accepts any pending change.
        /// </summary>
        public void AcceptChanges() => this.original = this.current;

        /// <summary>
        /// Rollbacks any pending change.
        /// </summary>
        public void Rollback() => this.current = this.original;

        /// <summary>
        /// Implicit operator for {T}.
        /// </summary>
        public static implicit operator T(ChangeTrackingProperty<T> t) =>
            t is null ? default : t.current;
    }
}
