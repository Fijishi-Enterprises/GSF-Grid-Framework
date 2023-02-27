﻿//******************************************************************************************************
//  IndependentAdapterManagerExtensions.cs - Gbtc
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
using System.Data;
using System.Linq;
using GSF.Data;
using GSF.Data.Model;
using GSF.TimeSeries.Data;
using GSF.TimeSeries.Model;
using DeviceRecord = GSF.TimeSeries.Model.Device;
using MeasurementRecord = GSF.TimeSeries.Model.Measurement;
using SignalTypeRecord = GSF.TimeSeries.Model.SignalType;
using HistorianRecord = GSF.TimeSeries.Model.Historian;
using SignalType = GSF.Units.EE.SignalType;

namespace GSF.TimeSeries.Adapters
{
    /// <summary>
    /// Represents an adapter base class that provides functionality to manage and distribute measurements to a collection of action adapters.
    /// </summary>
    public static class IndependentAdapterManagerExtensions
    {
        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.ConfigurationReloadWaitTimeout"/>.
        /// </summary>
        public const int DefaultConfigurationReloadWaitTimeout = 10000;

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.ConfigurationReloadWaitAttempts"/>.
        /// </summary>
        public const int DefaultConfigurationReloadWaitAttempts = 2;

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.DatabaseConnectionString"/>.
        /// </summary>
        public const string DefaultDatabaseConnectionString = "";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.DatabaseProviderString"/>.
        /// </summary>
        public const string DefaultDatabaseProviderString = "AssemblyName={System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089}; ConnectionType=System.Data.SqlClient.SqlConnection; AdapterType=System.Data.SqlClient.SqlDataAdapter";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.PointTagTemplate"/>.
        /// </summary>
        public const string DefaultPointTagTemplate = "IAM!{0}";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.AlternateTagTemplate"/>.
        /// </summary>
        public const string DefaultAlternateTagTemplate = "";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.SignalReferenceTemplate"/>.
        /// </summary>
        public const string DefaultSignalReferenceTemplate = $"{DefaultPointTagTemplate}-CV";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.DescriptionTemplate"/>.
        /// </summary>
        public const string DefaultDescriptionTemplate = "{0} [{1}] measurement created for {2} [{3}].";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.ParentDeviceAcronymTemplate"/>.
        /// </summary>
        public const string DefaultParentDeviceAcronymTemplate = "IAM!{0}";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.SignalType"/>.
        /// </summary>
        public const string DefaultSignalType = "CALC";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.TargetHistorianAcronym"/>.
        /// </summary>
        public const string DefaultTargetHistorianAcronym = "PPA";

        /// <summary>
        /// Defines the default value for the <see cref="IIndependentAdapterManager.SourceMeasurementTable"/>.
        /// </summary>
        public const string DefaultSourceMeasurementTable = "ActiveMeasurements";

        /// <summary>
        /// Finds child adapter with specified <paramref name="adapterName"/>.
        /// </summary>
        /// <param name="instance">Target <see cref="IIndependentAdapterManager"/> instance.</param>
        /// <param name="adapterName">Adapter name to find.</param>
        /// <returns><see cref="IAdapter"/> instance with <paramref name="adapterName"/>, if found; otherwise, <c>null</c>.</returns>
        public static IAdapter FindAdapter(this IIndependentAdapterManager instance, string adapterName) => 
            instance.FirstOrDefault(adapter => adapterName.Equals(adapter.Name));

        /// <summary>
        /// Lookups up point tag name from provided <paramref name="signalID"/>.
        /// </summary>
        /// <param name="instance">Target <see cref="IIndependentAdapterManager"/> instance.</param>
        /// <param name="signalID"><see cref="Guid"/> signal ID to lookup.</param>
        /// <param name="measurementTable">Measurement table name used for meta-data lookup.</param>
        /// <returns>Point tag name, if found; otherwise, string representation of provided signal ID.</returns>
        public static string LookupPointTag(this IIndependentAdapterManager instance, Guid signalID, string measurementTable = "ActiveMeasurements")
        {
            DataRow record = instance.DataSource.LookupMetadata(signalID, measurementTable);
            string pointTag = null;

            if (record is not null)
                pointTag = record["PointTag"].ToString();

            if (string.IsNullOrWhiteSpace(pointTag))
                pointTag = signalID.ToString();

            return pointTag.ToUpper();
        }

        /// <summary>
        /// Lookups up associated device name from provided <paramref name="signalID"/>.
        /// </summary>
        /// <param name="instance">Target <see cref="IIndependentAdapterManager"/> instance.</param>
        /// <param name="signalID"><see cref="Guid"/> signal ID to lookup.</param>
        /// <param name="measurementTable">Measurement table name used for meta-data lookup.</param>
        /// <returns>Device name, if found; otherwise, string representation of associated point tag.</returns>
        public static string LookupDevice(this IIndependentAdapterManager instance, Guid signalID, string measurementTable = "ActiveMeasurements")
        {
            DataRow record = instance.DataSource.LookupMetadata(signalID, measurementTable);
            string device = null;

            if (record is not null)
                device = record[nameof(Device)].ToString();

            if (string.IsNullOrWhiteSpace(device))
                device = instance.LookupPointTag(signalID, measurementTable);

            return device.ToUpper();
        }

        /// <summary>
        /// Lookups up associated phasor label from provided <paramref name="signalID"/>.
        /// </summary>
        /// <param name="instance">Target <see cref="IIndependentAdapterManager"/> instance.</param>
        /// <param name="signalID"><see cref="Guid"/> signal ID to lookup.</param>
        /// <param name="measurementTable">Measurement table name used for meta-data lookup.</param>
        /// <returns>Phasor label name, if found; otherwise, string representation associated point tag.</returns>
        public static string LookupPhasorLabel(this IIndependentAdapterManager instance, Guid signalID, string measurementTable = "ActiveMeasurements")
        {
            DataRow record = instance.DataSource.LookupMetadata(signalID, measurementTable);
            int phasorID = 0;

            if (record is not null)
                phasorID = record.ConvertNullableField<int>("PhasorID") ?? 0;

            if (phasorID == 0)
                return instance.LookupPointTag(signalID, measurementTable);

            using AdoDataConnection connection = instance.HandleGetConfiguredConnection();

            TableOperations<Phasor> phasorTable = new(connection);
            Phasor phasorRecord = phasorTable.QueryRecordWhere("ID = {0}", phasorID);
            return phasorRecord is null ? instance.LookupPointTag(signalID, measurementTable) : phasorRecord.Label.Trim().ToUpper();
        }

        /// <summary>
        /// Determines if <paramref name="signalID"/> exists in local configuration.
        /// </summary>
        /// <param name="instance">Target <see cref="IIndependentAdapterManager"/> instance.</param>
        /// <param name="signalID">Signal ID to find.</param>
        /// <param name="measurementTable">Measurement table name used for meta-data lookup.</param>
        /// <returns><c>true</c>, if <paramref name="signalID"/> is found; otherwise, <c>false</c>.</returns>
        public static bool SignalIDExists(this IIndependentAdapterManager instance, Guid signalID, string measurementTable = "ActiveMeasurements") => instance.DataSource.LookupMetadata(signalID, measurementTable) is not null;

        /// <summary>
        /// Gets measurement record, creating it if needed.
        /// </summary>
        /// <param name="instance">Target <see cref="IIndependentAdapterManager"/> instance.</param>
        /// <param name="currentDeviceID">Device ID associated with current adapter, or zero if none.</param>
        /// <param name="pointTag">Point tag of measurement.</param>
        /// <param name="alternateTag">Alternate tag of measurement.</param>
        /// <param name="signalReference">Signal reference of measurement.</param>
        /// <param name="description">Description of measurement.</param>
        /// <param name="signalType">Signal type of measurement.</param>
        /// <param name="targetHistorianAcronym">Acronym of target historian for measurement.</param>
        /// <returns>Measurement record.</returns>
        public static MeasurementRecord GetMeasurementRecord(this IIndependentAdapterManager instance, int currentDeviceID, string pointTag, string alternateTag, string signalReference, string description, SignalType signalType = SignalType.CALC, string targetHistorianAcronym = "PPA")
        {
            // Open database connection as defined in configuration file "systemSettings" category
            using AdoDataConnection connection = instance.GetConfiguredConnection();

            TableOperations<DeviceRecord> deviceTable = new(connection);
            TableOperations<MeasurementRecord> measurementTable = new(connection);
            TableOperations<HistorianRecord> historianTable = new(connection);
            TableOperations<SignalTypeRecord> signalTypeTable = new(connection);

            // Lookup target device ID
            int? deviceID = currentDeviceID > 0 ? currentDeviceID : deviceTable.QueryRecordWhere("Acronym = {0}", instance.Name)?.ID;

            // Lookup target historian ID
            int? historianID = historianTable.QueryRecordWhere("Acronym = {0}", targetHistorianAcronym)?.ID;

            // Lookup signal type ID
            int signalTypeID = signalTypeTable.QueryRecordWhere("Acronym = {0}", signalType.ToString())?.ID ?? 1;

            // Lookup measurement record by point tag, creating a new record if one does not exist
            MeasurementRecord measurement = measurementTable.QueryRecordWhere("SignalReference = {0}", signalReference) ?? measurementTable.NewRecord();

            // Update record fields
            measurement.DeviceID = deviceID;
            measurement.HistorianID = historianID;
            measurement.PointTag = pointTag;
            measurement.AlternateTag = alternateTag;
            measurement.SignalReference = signalReference;
            measurement.SignalTypeID = signalTypeID;
            measurement.Description = description;

            // Save record updates
            measurementTable.AddNewOrUpdateRecord(measurement);

            // Re-query new records to get any database assigned information, e.g., unique Guid-based signal ID
            if (measurement.PointID == 0)
                measurement = measurementTable.QueryRecordWhere("SignalReference = {0}", signalReference);

            // Notify host system of configuration changes
            instance.OnConfigurationChanged();

            return measurement;
        }

        /// <summary>
        /// Waits for <paramref name="signalIDs"/> to be loaded in system configuration.
        /// </summary>
        /// <param name="instance">Target <see cref="IIndependentAdapterManager"/> instance.</param>
        /// <param name="signalIDs">Signal IDs to wait for.</param>
        /// <param name="measurementTable">Measurement table name used for meta-data lookup.</param>
        public static void WaitForSignalsToLoad(this IIndependentAdapterManager instance, Guid[] signalIDs, string measurementTable = "ActiveMeasurements")
        {
            int attempts = 0;

            bool signalExists(Guid signalID) => instance.SignalIDExists(signalID, measurementTable);

            while (attempts++ < instance.ConfigurationReloadWaitAttempts)
            {
                instance.ConfigurationReloadedWaitHandle.Reset();

                if (signalIDs.All(signalExists))
                    break;

                instance.ConfigurationReloadedWaitHandle.Wait(instance.ConfigurationReloadWaitTimeout);
            }
        }
    }
}
