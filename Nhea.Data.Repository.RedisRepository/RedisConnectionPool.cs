using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading;

namespace Nhea.Data.Repository.RedisRepository
{
    internal class RedisConnectionPool
    {
        private static readonly Lock lockPoolRoundRobin = new();

        private Lazy<ConnectionMultiplexer>[] ConnectionMultiplexers = null;

        private Func<Lazy<ConnectionMultiplexer>, object> ConnectionMultiplexerFilter = null;

        private ConnectionVotingTypes ConnectionVotingType { get; set; }

        internal RedisConnectionPool(string connectionString, int poolSize, ConnectionVotingTypes connectionVotingType, Func<Lazy<ConnectionMultiplexer>, object> poolFilter)
        {
            this.ConnectionVotingType = connectionVotingType;
            ConnectionMultiplexerFilter = poolFilter;

            lock (lockPoolRoundRobin)
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
                lock (lockPoolRoundRobin)
                {
                    var lazyLoadedConnections = ConnectionMultiplexers.Where((lazy) => lazy.IsValueCreated && lazy.Value.IsConnected);

                    if (lazyLoadedConnections.Count() == ConnectionMultiplexers.Length)
                    {
                        if (this.ConnectionVotingType == ConnectionVotingTypes.LeastLoaded)
                        {
                            return lazyLoadedConnections.OrderBy(query => query.Value.GetCounters().TotalOutstanding).First().Value;
                        }
                        else if (this.ConnectionVotingType == ConnectionVotingTypes.Random)
                        {
                            return lazyLoadedConnections.OrderBy(query => Guid.NewGuid()).First().Value;
                        }
                        else
                        {
                            return lazyLoadedConnections.OrderBy(this.ConnectionMultiplexerFilter).First().Value;
                        }
                    }
                    else
                    {
                        return ConnectionMultiplexers[lazyLoadedConnections.Count()].Value;
                    }
                }
            }
        }
    }
}
