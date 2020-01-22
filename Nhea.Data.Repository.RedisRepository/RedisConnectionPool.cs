using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nhea.Data.Repository.RedisRepository
{
    internal class RedisConnectionPool
    {
        private static readonly object lockPookRoundRobin = new object();

        private Lazy<ConnectionMultiplexer>[] ConnectionMultiplexers = null;

        private Func<Lazy<ConnectionMultiplexer>, object> ConnectionMultiplexerFilter = null;

        private ConnectionVotingTypes ConnectionVotingType { get; set; }

        internal RedisConnectionPool(string connectionString, int poolSize, ConnectionVotingTypes connectionVotingType, Func<Lazy<ConnectionMultiplexer>, object> poolFilter)
        {
            this.ConnectionVotingType = connectionVotingType;
            ConnectionMultiplexerFilter = poolFilter;

            lock (lockPookRoundRobin)
            {
                if (ConnectionMultiplexers == null)
                {
                    ConnectionMultiplexers = new Lazy<ConnectionMultiplexer>[poolSize];
                }

                for (int i = 0; i < poolSize; i++)
                {
                    if (ConnectionMultiplexers[i] == null)
                    {
                        var connectionMultiplexer = ConnectionMultiplexer.Connect(connectionString);

                        ConnectionMultiplexers[i] = new Lazy<ConnectionMultiplexer>(() => connectionMultiplexer);
                    }
                }
            }
        }

        internal ConnectionMultiplexer Connection
        {
            get
            {
                lock (lockPookRoundRobin)
                {
                    var loadedLazys = ConnectionMultiplexers.Where((lazy) => lazy.IsValueCreated && lazy.Value.IsConnected);

                    if (loadedLazys.Count() == ConnectionMultiplexers.Count())
                    {
                        if (this.ConnectionVotingType == ConnectionVotingTypes.LeastLoaded)
                        {
                            return loadedLazys.OrderBy(query => query.Value.GetCounters().TotalOutstanding).First().Value;
                        }
                        else if (this.ConnectionVotingType == ConnectionVotingTypes.Random)
                        {
                            return loadedLazys.OrderBy(query => Guid.NewGuid()).First().Value;
                        }
                        else
                        {
                            return loadedLazys.OrderBy(this.ConnectionMultiplexerFilter).First().Value;
                        }
                    }
                    else
                    {
                        return ConnectionMultiplexers[loadedLazys.Count()].Value;
                    }
                }
            }
        }
    }
}
