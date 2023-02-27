﻿//******************************************************************************************************
//  IndependentOutputAdapterManagerBase.cs - Gbtc
//
//  Copyright © 2020, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  02/13/2020 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Text;
using System.Threading;
using GSF.Data;
using GSF.Diagnostics;
using GSF.TimeSeries.Data;
using GSF.Units.EE;
using static GSF.TimeSeries.Adapters.IndependentAdapterManagerExtensions;

namespace GSF.TimeSeries.Adapters
{
    /// <summary>
    /// Represents an adapter base class that provides functionality to manage and distribute measurements to a collection of output adapters.
    /// </summary>
    public abstract class IndependentOutputAdapterManagerBase : OutputAdapterCollection, IIndependentAdapterManager
    {
        #region [ Members ]

        // Fields
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="IndependentOutputAdapterManagerBase"/>.
        /// </summary>
        protected IndependentOutputAdapterManagerBase() => this.HandleConstruct();

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets primary keys of input measurements for the <see cref="IndependentOutputAdapterManagerBase"/>.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines primary keys of input measurements the adapter expects; can be one of a filter expression, measurement key, point tag or Guid.")]
        [CustomConfigurationEditor("GSF.TimeSeries.UI.WPF.dll", "GSF.TimeSeries.UI.Editors.MeasurementEditor")]
        [DefaultValue(null)]
        public override MeasurementKey[] InputMeasurementKeys
        {
            get => base.InputMeasurementKeys;
            set
            {
                base.InputMeasurementKeys = value;
                InputMeasurementKeyTypes = DataSource.GetSignalTypes(value, SourceMeasurementTable);
            }
        }

        /// <summary>
        /// Gets or sets the wait timeout, in milliseconds, that system wait for system configuration reload to complete.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the wait timeout, in milliseconds, that system wait for system configuration reload to complete.")]
        [DefaultValue(DefaultConfigurationReloadWaitTimeout)]
        public virtual int ConfigurationReloadWaitTimeout { get; set; } = DefaultConfigurationReloadWaitTimeout;

        /// <summary>
        /// Gets or sets the total number of attempts to wait for system configuration reloads when waiting for configuration updates to be available.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the total number of attempts to wait for system configuration reloads when waiting for configuration updates to be available.")]
        [DefaultValue(DefaultConfigurationReloadWaitAttempts)]
        public virtual int ConfigurationReloadWaitAttempts { get; set; } = DefaultConfigurationReloadWaitAttempts;

        /// <summary>
        /// Gets or sets the connection string used for database operations. Leave blank to use local configuration database defined in "systemSettings".
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the connection string used for database operations. Leave blank to use local configuration database defined in \"systemSettings\".")]
        [DefaultValue(DefaultDatabaseConnectionString)]
        public virtual string DatabaseConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the provider string used for database operations. Defaults to a SQL Server provider string.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the provider string used for database operations. Defaults to a SQL Server provider string.")]
        [DefaultValue(DefaultDatabaseProviderString)]
        public virtual string DatabaseProviderString { get; set; }

        /// <summary>
        /// Gets any custom adapter settings to be added to each adapter connection string. Can be used to add
        /// settings that are custom per adapter.
        /// </summary>
        public virtual string CustomAdapterSettings { get; } = null;

        /// <summary>
        /// Gets or sets the source measurement table to use for configuration.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the source measurement table to use for configuration.")]
        [DefaultValue(DefaultSourceMeasurementTable)]
        public virtual string SourceMeasurementTable { get; set; } = DefaultSourceMeasurementTable;

        /// <summary>
        /// Gets or sets <see cref="DataSet"/> based data source used to load each <see cref="IAdapter"/>. Updates
        /// to this property will cascade to all adapters in this <see cref="IndependentOutputAdapterManagerBase"/>.
        /// </summary>
        public override DataSet DataSource
        {
            get => base.DataSource;
            set
            {
                if (!this.DataSourceChanged(value))
                    return;

                base.DataSource = value;
                this.HandleUpdateDataSource();
            }
        }

        /// <summary>
        /// Gets number of input measurement required by each adapter.
        /// </summary>
        public abstract int PerAdapterInputCount { get; }

        /// <summary>
        /// Gets or sets the index into the per adapter input measurements to use for target adapter name.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the index into the per adapter input measurements to use for target adapter name.")]
        public virtual int InputMeasurementIndexUsedForName { get; set; }

        /// <summary>
        /// Gets or sets flag that determines if the <see cref="IndependentOutputAdapterManagerBase"/> adapter
        /// <see cref="AdapterCollectionBase{T}.ConnectionString"/> should be automatically parsed every time
        /// the <see cref="DataSource"/> is updated without requiring adapter to be reinitialized. Defaults
        /// to <c>true</c> to allow child adapters to come and go based on updates to system configuration.
        /// </summary>
        protected virtual bool AutoReparseConnectionString { get; set; } = true;

        /// <summary>
        /// Gets input measurement <see cref="SignalType"/>'s for each of the <see cref="AdapterBase.InputMeasurementKeys"/>, if any.
        /// </summary>
        public virtual SignalType[] InputMeasurementKeyTypes { get; private set; }

        /// <summary>
        /// Get adapter index currently being processed.
        /// </summary>
        public int CurrentAdapterIndex { get; internal set; }

        /// <summary>
        /// Get adapter output index currently being processed.
        /// </summary>
        public int CurrentOutputIndex { get; internal set; }

        /// <summary>
        /// Gets associated device ID for <see cref="CurrentAdapterIndex"/>, if any, for measurement generation.
        /// </summary>
        public virtual int CurrentDeviceID { get; } = 0;

        /// <summary>
        /// Returns the detailed status of the <see cref="IndependentOutputAdapterManagerBase"/>.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new();

                status.Append(this.HandleStatus());
                status.Append(base.Status);

                return status.ToString();
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="IndependentOutputAdapterManagerBase"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (m_disposed)
                return;

            try
            {
                if (!disposing)
                    return;

                this.HandleDispose();
            }
            finally
            {
                m_disposed = true;          // Prevent duplicate dispose.
                base.Dispose(disposing);    // Call base class Dispose().
            }
        }

        /// <summary>
        /// Initializes the <see cref="IndependentOutputAdapterManagerBase" />.
        /// </summary>
        public override void Initialize() => this.HandleInitialize();

        /// <summary>
        /// Validates that an even number of inputs are provided for specified <see cref="PerAdapterInputCount"/>.
        /// </summary>
        protected void ValidateEvenInputCount() => this.HandleValidateEvenInputCount();

        /// <summary>
        /// Parses connection string. Derived classes should override for custom connection string parsing.
        /// </summary>
        public virtual void ParseConnectionString() => this.HandleParseConnectionString();

        /// <summary>
        /// Notifies derived classes that configuration has been reloaded
        /// </summary>
        public virtual void ConfigurationReloaded() { }

        /// <summary>
        /// Recalculates routing tables.
        /// </summary>
        public virtual void RecalculateRoutingTables() => this.HandleRecalculateRoutingTables();

        /// <summary>
        /// Queues a collection of measurements for processing to each <see cref="IOutputAdapter"/> connected to this <see cref="IndependentOutputAdapterManagerBase"/>.
        /// </summary>
        /// <param name="measurements">Measurements to queue for processing.</param>
        public override void QueueMeasurementsForProcessing(IEnumerable<IMeasurement> measurements) => this.HandleQueueMeasurementsForProcessing(measurements);

        /// <summary>
        /// Gets a short one-line status of this <see cref="IndependentOutputAdapterManagerBase"/>.
        /// </summary>
        /// <param name="maxLength">Maximum number of available characters for display.</param>
        /// <returns>A short one-line summary of the current status of the <see cref="IndependentOutputAdapterManagerBase"/>.</returns>
        public override string GetShortStatus(int maxLength) => this.HandleGetShortStatus(maxLength);

        /// <summary>
        /// Enumerates child adapters.
        /// </summary>
        [AdapterCommand("Enumerates child adapters.")]
        public virtual void EnumerateAdapters() => this.HandleEnumerateAdapters();

        /// <summary>
        /// Gets subscriber information for specified client connection.
        /// </summary>
        /// <param name="adapterIndex">Enumerated index for child adapter.</param>
        /// <returns>Status for adapter with specified <paramref name="adapterIndex"/>.</returns>
        [AdapterCommand("Gets subscriber information for specified client connection.")]
        public virtual string GetAdapterStatus(int adapterIndex) => this.HandleGetAdapterStatus(adapterIndex);

        /// <summary>
        /// Gets configured database connection.
        /// </summary>
        /// <returns>New ADO data connection based on configured settings.</returns>
        public AdoDataConnection GetConfiguredConnection() => this.HandleGetConfiguredConnection();

        #endregion

        #region [ IIndependentAdapterManager Implementation ]

        // The following properties are not used by output adapters. An IOutputAdapter implementation does not define
        // output measurements nor is any output automatically reentrant into time-series framework.

        string IIndependentAdapterManager.PointTagTemplate { get; set; } = DefaultPointTagTemplate;

        string IIndependentAdapterManager.AlternateTagTemplate { get; set; } = DefaultAlternateTagTemplate;

        string IIndependentAdapterManager.SignalReferenceTemplate { get; set; } = DefaultSignalReferenceTemplate;

        string IIndependentAdapterManager.DescriptionTemplate { get; set; } = DefaultDescriptionTemplate;

        string IIndependentAdapterManager.ParentDeviceAcronymTemplate { get; set; } = DefaultParentDeviceAcronymTemplate;

        SignalType IIndependentAdapterManager.SignalType { get; set; } = (SignalType)Enum.Parse(typeof(SignalType), DefaultSignalType);

        SignalType[] IIndependentAdapterManager.SignalTypes { get; } = null;

        ReadOnlyCollection<string> IIndependentAdapterManager.PerAdapterOutputNames { get; } = null;

        SignalType[] IIndependentAdapterManager.OutputMeasurementTypes { get; } = null;

        string IIndependentAdapterManager.TargetHistorianAcronym { get; set; } = DefaultTargetHistorianAcronym;

        RoutingTables IIndependentAdapterManager.RoutingTables { get; set; }

        string IIndependentAdapterManager.OriginalDataMember { get; set; }

        uint IIndependentAdapterManager.AdapterIDCounter { get; set; }

        ManualResetEventSlim IIndependentAdapterManager.ConfigurationReloadedWaitHandle { get; set; }

        bool IIndependentAdapterManager.AutoReparseConnectionString { get => AutoReparseConnectionString; set => AutoReparseConnectionString = value; }

        void IIndependentAdapterManager.OnConfigurationChanged() => OnConfigurationChanged();

        void IIndependentAdapterManager.OnInputMeasurementKeysUpdated() => OnInputMeasurementKeysUpdated();

        void IIndependentAdapterManager.OnStatusMessage(MessageLevel level, string status, string eventName, MessageFlags flags) => OnStatusMessage(level, status, eventName, flags);

        void IIndependentAdapterManager.OnProcessException(MessageLevel level, Exception exception, string eventName, MessageFlags flags) => OnProcessException(level, exception, eventName, flags);

        #endregion
    }
}
