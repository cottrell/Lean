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
using System.Threading;
using Moq;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.Results;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Util;

namespace QuantConnect.Tests.Engine.DataFeeds
{
    [TestFixture]
    public class LiveCoarseUniverseTests
    {
        [Test]
        public void CoarseUniverseRotatesActiveSecurity()
        {
            var startDate = new DateTime(2014, 3, 24);
            var endDate = new DateTime(2014, 3, 28);

            var timeProvider = new ManualTimeProvider(TimeZones.NewYork);
            timeProvider.SetCurrentTime(startDate);

            var coarseTimes = new List<DateTime>
            {
                new DateTime(2014, 3, 24, 1, 0, 0, 0),
                new DateTime(2014, 3, 25, 5, 0, 0, 0),
                new DateTime(2014, 3, 26, 5, 0, 0, 0),
                new DateTime(2014, 3, 27, 5, 0, 0, 0),
                new DateTime(2014, 3, 28, 5, 0, 0, 0)
            }.ToHashSet();

            var coarseSymbols = new List<Symbol> { Symbols.SPY, Symbols.AAPL, Symbols.MSFT };

            var emitted = new AutoResetEvent(false);
            var dataQueueHandler = new FuncDataQueueHandler(fdqh => Enumerable.Empty<BaseData>(), timeProvider);

            var feed = new TestableLiveTradingDataFeed(dataQueueHandler);

            var algorithm = new AlgorithmStub(feed);
            algorithm.SetLiveMode(true);

            var mock = new Mock<ITransactionHandler>();
            mock.Setup(m => m.GetOpenOrders(It.IsAny<Func<Order, bool>>())).Returns(new List<Order>());
            algorithm.Transactions.SetOrderProcessor(mock.Object);

            var synchronizer = new TestableLiveSynchronizer(timeProvider);
            synchronizer.Initialize(algorithm, algorithm.DataManager);

            var mapFileProvider = new LocalDiskMapFileProvider();
            feed.Initialize(algorithm, new LiveNodePacket(), new BacktestingResultHandler(),
                mapFileProvider, new LocalDiskFactorFileProvider(mapFileProvider), new DefaultDataProvider(), algorithm.DataManager, synchronizer, new DataChannelProvider());

            var symbolIndex = 0;
            var coarseUniverseSelectionCount = 0;
            algorithm.AddUniverse(
                coarse =>
                {
                    Interlocked.Increment(ref coarseUniverseSelectionCount);
                    emitted.Set();

                    // rotate single symbol in universe
                    if (symbolIndex == coarseSymbols.Count) symbolIndex = 0;

                    return new[] { coarseSymbols[symbolIndex++] };
                });

            algorithm.PostInitialize();

            var cancellationTokenSource = new CancellationTokenSource();

            Exception exceptionThrown = null;

            // create a timer to advance time much faster than realtime
            var timerInterval = TimeSpan.FromMilliseconds(5);
            var timer = Ref.Create<Timer>(null);
            timer.Value = new Timer(state =>
            {
                try
                {
                    var currentTime = timeProvider.GetUtcNow().ConvertFromUtc(TimeZones.NewYork);

                    if (currentTime.Date > endDate.Date)
                    {
                        feed.Exit();
                        cancellationTokenSource.Cancel();
                        return;
                    }

                    timeProvider.Advance(TimeSpan.FromHours(1));

                    var time = timeProvider.GetUtcNow().ConvertFromUtc(TimeZones.NewYork);
                    if (coarseTimes.Contains(time))
                    {
                        // lets wait for coarse to emit
                        if (!emitted.WaitOne(TimeSpan.FromMilliseconds(10000)))
                        {
                            throw new TimeoutException("Timeout waiting for coarse to emit");
                        }
                    }
                    var activeSecuritiesCount = algorithm.ActiveSecurities.Count;

                    Assert.That(activeSecuritiesCount <= 1);

                    // restart the timer
                    timer.Value.Change(timerInterval, Timeout.InfiniteTimeSpan);
                }
                catch (Exception exception)
                {
                    Log.Error(exception);
                    exceptionThrown = exception;

                    feed.Exit();
                    cancellationTokenSource.Cancel();
                }

            }, null, timerInterval, Timeout.InfiniteTimeSpan);

            foreach (var _ in synchronizer.StreamData(cancellationTokenSource.Token)) { }

            timer.Value.DisposeSafely();
            algorithm.DataManager.RemoveAllSubscriptions();
            dataQueueHandler.DisposeSafely();
            synchronizer.DisposeSafely();
            emitted.DisposeSafely();

            if (exceptionThrown != null)
            {
                throw new Exception("Exception in timer: ", exceptionThrown);
            }

            Assert.AreEqual(coarseTimes.Count, coarseUniverseSelectionCount, message: "coarseUniverseSelectionCount");
        }
    }
}