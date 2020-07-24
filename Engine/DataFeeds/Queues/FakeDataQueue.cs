﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Util;
using Timer = System.Timers.Timer;

namespace QuantConnect.Lean.Engine.DataFeeds.Queues
{
    /// <summary>
    /// This is an implementation of <see cref="IDataQueueHandler"/> used for testing
    /// </summary>
    public class FakeDataQueue : IDataQueueHandler
    {
        private int count;
        private readonly Random _random = new Random();

        private readonly Timer _timer;
        private readonly HashSet<Symbol> _symbols;
        private readonly object _sync = new object();
        private readonly IDataAggregator _aggregator;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeDataQueue"/> class to randomly emit data for each symbol
        /// </summary>
        public FakeDataQueue()
        {
            _aggregator = new AggregationManager();
            _symbols = new HashSet<Symbol>();

            // load it up to start
            PopulateQueue();
            PopulateQueue();
            PopulateQueue();
            PopulateQueue();

            _timer = new Timer
            {
                AutoReset = true,
                Enabled = true,
                Interval = 1000,
            };

            var lastCount = 0;
            var lastTime = DateTime.Now;
            _timer.Elapsed += (sender, args) =>
            {
                var elapsed = (DateTime.Now - lastTime);
                var ticksPerSecond = (count - lastCount)/elapsed.TotalSeconds;
                Console.WriteLine("TICKS PER SECOND:: " + ticksPerSecond.ToStringInvariant("000000.0") + " ITEMS IN QUEUE:: " + 0);
                lastCount = count;
                lastTime = DateTime.Now;
                PopulateQueue();
            };
        }

        /// <summary>
        /// Subscribe to the specified configuration
        /// </summary>
        /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
        /// <param name="newDataAvailableHandler">handler to be fired on new data available</param>
        /// <returns>The new enumerator for this subscription request</returns>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            lock (_sync)
            {
                _symbols.Add(dataConfig.Symbol);
            }

            return enumerator;
        }

        /// <summary>
        /// Removes the specified configuration
        /// </summary>
        /// <param name="dataConfig">Subscription config to be removed</param>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            lock (_sync)
            {
                _symbols.Remove(dataConfig.Symbol);
            }
            _aggregator.Remove(dataConfig);
        }

        /// <summary>
        /// Returns whether the data provider is connected
        /// </summary>
        /// <returns>true if the data provider is connected</returns>
        public bool IsConnected => true;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _timer.Stop();
            _timer.DisposeSafely();
        }

        /// <summary>
        /// Pumps a bunch of ticks into the queue
        /// </summary>
        private void PopulateQueue()
        {
            List<Symbol> symbols;
            lock (_sync)
            {
                symbols = _symbols.ToList();
            }

            foreach (var symbol in symbols)
            {
                // emits 500k per second
                for (var i = 0; i < 500000; i++)
                {
                    _aggregator.Update(new Tick
                    {
                        Time = DateTime.Now,
                        Symbol = symbol,
                        Value = 10 + (decimal)Math.Abs(Math.Sin(DateTime.Now.TimeOfDay.TotalMinutes)),
                        TickType = TickType.Trade,
                        Quantity = _random.Next(10, (int)_timer.Interval)
                    });
                }
            }
        }
    }
}
