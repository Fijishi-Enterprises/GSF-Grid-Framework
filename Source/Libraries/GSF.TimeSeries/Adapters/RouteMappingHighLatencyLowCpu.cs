﻿//******************************************************************************************************
//  RoutingMappingHighLatencyLowCpu.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  06/29/2016 - Steven E. Chisholm
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GSF.Collections;
using GSF.Diagnostics;
using GSF.Threading;

namespace GSF.TimeSeries.Adapters
{
    /// <summary>
    /// Represents an alternative routing table that has intentional delays to lower overall CPU utilization.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public class RouteMappingHighLatencyLowCpu : IRouteMappingTables
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
        private class Consumer
        {
            private readonly ScheduledTask m_task;
            private readonly Action<IEnumerable<IMeasurement>> m_callback;
            private readonly ConcurrentQueue<List<IMeasurement>> m_pendingMeasurements;
            public List<IMeasurement> MeasurementsToRoute;
            public readonly RoutingPassthroughMethod Methods;

            public Consumer(IAdapter adapter)
            {
                Methods = (adapter as IOptimizedRoutingConsumer)?.GetRoutingPassthroughMethods();

                if (Methods is not null)
                    return;

                m_callback = adapter is IActionAdapter actionAdapter ? 
                    actionAdapter.QueueMeasurementsForProcessing :
                    ((IOutputAdapter)adapter).QueueMeasurementsForProcessing;

                m_task = new ScheduledTask();
                m_task.Running += m_task_Running;
                m_pendingMeasurements = new ConcurrentQueue<List<IMeasurement>>();
                MeasurementsToRoute = new List<IMeasurement>();
            }

            public void RoutingComplete()
            {
                if (MeasurementsToRoute.Count <= 0)
                    return;

                m_pendingMeasurements.Enqueue(MeasurementsToRoute);
                MeasurementsToRoute = new List<IMeasurement>();
                m_task.Start();
            }

            private void m_task_Running(object sender, EventArgs<ScheduledTaskRunningReason> e)
            {
                if (e.Argument == ScheduledTaskRunningReason.Disposing)
                    return;

                while (m_pendingMeasurements.TryDequeue(out List<IMeasurement> measurements))
                    m_callback(measurements);
            }
        }

        private class GlobalCache
        {
            /// <summary>
            /// Contains all consumers for all adapters regardless of if they are optimized or not.
            /// </summary>
            public readonly Dictionary<IAdapter, Consumer> GlobalDestinationLookup;

            //Routing Destinations. Each consumer is only represented once.
            //Note: this is a change from the previous adapter where broadcast adapters were put their 
            //signal lookups
            public readonly IndexedArray<List<Consumer>> GlobalSignalLookup;
            public readonly List<Consumer> BroadcastConsumers;
            public readonly List<RoutingPassthroughMethod> RoutingPassthroughAdapters;
            /// <summary>
            /// Normal as opposed to optimized ones;
            /// </summary>
            public readonly List<Consumer> NormalDestinationAdapters;
            public readonly int Version;

            public GlobalCache(Dictionary<IAdapter, Consumer> consumers, int version)
            {
                NormalDestinationAdapters = new List<Consumer>();
                RoutingPassthroughAdapters = new List<RoutingPassthroughMethod>();
                GlobalSignalLookup = new IndexedArray<List<Consumer>>();
                BroadcastConsumers = new List<Consumer>();
                GlobalDestinationLookup = consumers;
                Version = version;

                // Generate routes for all signals received by each consumer adapter
                foreach (KeyValuePair<IAdapter, Consumer> kvp in consumers)
                {
                    IAdapter consumerAdapter = kvp.Key;
                    Consumer consumer = kvp.Value;
                    
                    if (consumer.Methods is not null)
                    {
                        RoutingPassthroughAdapters.Add(consumer.Methods);
                    }
                    else
                    {
                        NormalDestinationAdapters.Add(consumer);
                        
                        if (consumerAdapter.InputMeasurementKeys is not null)
                        {
                            // Create routes for each of the consumer's input signals
                            foreach (MeasurementKey key in consumerAdapter.InputMeasurementKeys)
                            {
                                List<Consumer> list = GlobalSignalLookup[key.RuntimeID];
                                
                                if (list is null)
                                {
                                    list = new List<Consumer>();
                                    GlobalSignalLookup[key.RuntimeID] = list;
                                }

                                list.Add(consumer);
                            }
                        }
                        else
                        {
                            // Add this consumer to the broadcast routes to begin receiving all measurements
                            BroadcastConsumers.Add(consumer);
                        }
                    }
                }
            }
        }

        // Fields
        private readonly ScheduledTask m_task;
        private readonly ConcurrentQueue<List<IMeasurement>> m_inboundQueue;
        private long m_measurementsRoutedInputFrames;
        private long m_measurementsRoutedInputMeasurements;
        private long m_measurementsRoutedOutput;
        private long m_routeOperations;
        private readonly int m_routeLatency;
        private readonly int m_batchSize;

        private GlobalCache m_globalCache;
        private Action<string> m_onStatusMessage;
        private Action<Exception> m_onProcessException;
        private ShortTime m_lastStatusUpdate;

        private int m_pendingMeasurements;
        /// <summary>
        /// Once this many measurements have been queued, a route operation will not wait the mandatory wait time
        /// and will immediately start the routing process.
        /// </summary>
        private readonly int m_maxPendingMeasurements;

        private readonly LogPublisher Log = Logger.CreatePublisher(typeof(RouteMappingHighLatencyLowCpu), MessageClass.Framework);

        /// <summary>
        /// Creates a <see cref="RouteMappingHighLatencyLowCpu"/>
        /// </summary>
        public RouteMappingHighLatencyLowCpu()
        {
            m_lastStatusUpdate = ShortTime.Now;
            m_maxPendingMeasurements = 1000;
            m_routeLatency = OptimizationOptions.RoutingLatency;
            m_batchSize = OptimizationOptions.RoutingBatchSize;
            m_inboundQueue = new ConcurrentQueue<List<IMeasurement>>();

            m_task = new ScheduledTask(ThreadingMode.DedicatedBackground, ThreadPriority.AboveNormal);
            m_task.Running += m_task_Running;
            m_task.UnhandledException += m_task_UnhandledException;
            m_task.Disposing += m_task_Disposing;
            m_task.Start(m_routeLatency);

            m_onStatusMessage = _ => { };
            m_onProcessException = _ => { };
            m_globalCache = new GlobalCache(new Dictionary<IAdapter, Consumer>(), 0);
            RouteCount = m_globalCache.GlobalSignalLookup.Count(x => x is not null);
        }

        /// <summary>
        /// Gets the number of routes in this routing table.
        /// </summary>
        public int RouteCount { get; private set; }

        /// <summary>
        /// Assigns the status messaging callbacks.
        /// </summary>
        /// <param name="onStatusMessage">Raise status messages on this callback</param>
        /// <param name="onProcessException">Raise exceptions on this callback</param>
        public void Initialize(Action<string> onStatusMessage, Action<Exception> onProcessException)
        {
            m_onStatusMessage = onStatusMessage ?? throw new ArgumentNullException(nameof(onStatusMessage));
            m_onProcessException = onProcessException ?? throw new ArgumentNullException(nameof(onProcessException));
        }

        /// <summary>
        /// Patches the existing routing table with the supplied adapters.
        /// </summary>
        /// <param name="producerAdapters">all of the producers</param>
        /// <param name="consumerAdapters">all of the consumers</param>
        public void PatchRoutingTable(RoutingTablesAdaptersList producerAdapters, RoutingTablesAdaptersList consumerAdapters)
        {
            if (producerAdapters is null)
                throw new ArgumentNullException(nameof(producerAdapters));
            
            if (consumerAdapters is null)
                throw new ArgumentNullException(nameof(consumerAdapters));

            foreach (IAdapter producerAdapter in producerAdapters.NewAdapter)
            {
                if (producerAdapter is IInputAdapter inputAdapter)
                    inputAdapter.NewMeasurements += Route;
                else if (producerAdapter is IActionAdapter actionAdapter)
                    actionAdapter.NewMeasurements += Route;
            }

            foreach (IAdapter producerAdapter in producerAdapters.OldAdapter)
            {
                if (producerAdapter is IInputAdapter inputAdapter)
                    inputAdapter.NewMeasurements -= Route;
                else if (producerAdapter is IActionAdapter actionAdapter)
                    actionAdapter.NewMeasurements -= Route;
            }

            Dictionary<IAdapter, Consumer> consumerLookup = new(m_globalCache.GlobalDestinationLookup);

            foreach (IAdapter consumerAdapter in consumerAdapters.NewAdapter)
                consumerLookup.Add(consumerAdapter, new Consumer(consumerAdapter));

            foreach (IAdapter consumerAdapter in consumerAdapters.OldAdapter)
                consumerLookup.Remove(consumerAdapter);

            m_globalCache = new GlobalCache(consumerLookup, m_globalCache.Version + 1);
            RouteCount = m_globalCache.GlobalSignalLookup.Count(x => x is not null);
        }

        private void m_task_Disposing(object sender, EventArgs e) => 
            m_onProcessException(new Exception("Routing table disposing."));

        private void m_task_UnhandledException(object sender, EventArgs<Exception> e) => 
            m_onProcessException(e.Argument);

        private void m_task_Running(object sender, EventArgs<ScheduledTaskRunningReason> e)
        {
            if (e.Argument == ScheduledTaskRunningReason.Disposing)
                return;

            m_task.Start(m_routeLatency);

            m_routeOperations++;

            if (m_lastStatusUpdate.ElapsedSeconds() > 15)
            {
                m_lastStatusUpdate = ShortTime.Now;
                Log.Publish(MessageLevel.Info, MessageFlags.None, "Routing Update",
                    $"Route Operations: {m_routeOperations}, Input Frames: {m_measurementsRoutedInputFrames}, Input Measurements: {m_measurementsRoutedInputMeasurements}, Output Measurements: {m_measurementsRoutedOutput}");
            }

            GlobalCache map = m_globalCache;

            try
            {
                int measurementsRouted = 0;

                while (m_inboundQueue.TryDequeue(out List<IMeasurement> measurements))
                {
                    measurementsRouted += measurements.Count;
                    Interlocked.Add(ref m_pendingMeasurements, -measurements.Count);

                    //For loops are faster than ForEach for List<T>
                    
                    //Process Optimized Consumers
                    for (int x = 0; x < map.RoutingPassthroughAdapters.Count; x++)
                        map.RoutingPassthroughAdapters[x].ProcessMeasurementList(measurements);

                    //Process Broadcast Consumers
                    for (int x = 0; x < map.BroadcastConsumers.Count; x++)
                    {
                        m_measurementsRoutedOutput += measurements.Count;
                        map.BroadcastConsumers[x].MeasurementsToRoute.AddRange(measurements);
                    }

                    m_measurementsRoutedInputFrames++;
                    m_measurementsRoutedInputMeasurements += measurements.Count;
                    
                    for (int x = 0; x < measurements.Count; x++)
                    {
                        IMeasurement measurement = measurements[x];
                        List<Consumer> consumers = map.GlobalSignalLookup[measurement.Key.RuntimeID];
                        
                        if (consumers is not null)
                        {
                            for (int i = 0; i < consumers.Count; i++)
                            {
                                m_measurementsRoutedOutput++;
                                consumers[i].MeasurementsToRoute.Add(measurement);
                            }
                        }
                    }

                    //If any adapter has too many measurements on their batch
                    //Route all adapter's measurements
                    if (measurementsRouted > m_batchSize)
                    {
                        measurementsRouted = 0;
                        
                        foreach (Consumer consumer in map.NormalDestinationAdapters)
                        {
                            measurementsRouted = Math.Max(measurementsRouted, consumer.MeasurementsToRoute.Count);
                            
                            if (consumer.MeasurementsToRoute.Count > m_batchSize)
                            {
                                foreach (Consumer c2 in map.NormalDestinationAdapters)
                                    c2.RoutingComplete();
                                
                                measurementsRouted = 0;
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (Consumer consumer in map.NormalDestinationAdapters)
                    consumer.RoutingComplete();
            }
        }
        
        private void Route(List<IMeasurement> measurements)
        {
            if (measurements.Count <= 0)
                return;

            m_inboundQueue.Enqueue(measurements);
            
            if (Interlocked.Add(ref m_pendingMeasurements, measurements.Count) > m_maxPendingMeasurements)
                m_task.Start();
        }

        private void Route(object sender, EventArgs<ICollection<IMeasurement>> measurements)
        {
            if (measurements?.Argument is null)
                return;

            Route(measurements.Argument as List<IMeasurement> ?? measurements.Argument.ToList());
        }

        /// <summary>
        /// This method will directly inject measurements into the routing table and use a shared local input adapter. For
        /// contention reasons, it is not recommended this be its default use case, but it is necessary at times.
        /// </summary>
        /// <param name="sender">the sender object</param>
        /// <param name="measurements">the event arguments</param>
        public void InjectMeasurements(object sender, EventArgs<ICollection<IMeasurement>> measurements) => 
            Route(sender, measurements);
    }
}
