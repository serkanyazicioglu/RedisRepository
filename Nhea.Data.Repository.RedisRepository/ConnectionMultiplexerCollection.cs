using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Nhea.Data.Repository.RedisRepository
{
    internal static class ConnectionMultiplexerCollection
    {
        internal static ConcurrentDictionary<string, ConnectionMultiplexer> ConnectionMultiplexers { get; set; }

        internal static ConcurrentDictionary<string, ConnectionMultiplexer> SubscriptionConnectionMultiplexers { get; set; }

        static ConnectionMultiplexerCollection()
        {
            ConnectionMultiplexers = new ConcurrentDictionary<string, ConnectionMultiplexer>();
            SubscriptionConnectionMultiplexers = new ConcurrentDictionary<string, ConnectionMultiplexer>();
        }

        private static object LockObject = new object();

        internal static ConnectionMultiplexer GetConnection(string connectionString)
        {
            if (!ConnectionMultiplexers.ContainsKey(connectionString))
            {
                lock (LockObject)
                {
                    if (!ConnectionMultiplexers.ContainsKey(connectionString))
                    {
                        return ConnectionMultiplexers.GetOrAdd(connectionString, ConnectionMultiplexer.Connect(connectionString));
                    }
                    else
                    {
                        return ConnectionMultiplexers[connectionString];
                    }
                }
            }
            else
            {
                return ConnectionMultiplexers[connectionString];
            }
        }

        private static object SubscriptionLockObject = new object();

        internal static ConnectionMultiplexer GetSubscriptionConnection(string connectionString)
        {
            if (!SubscriptionConnectionMultiplexers.ContainsKey(connectionString))
            {
                lock (SubscriptionLockObject)
                {
                    if (!SubscriptionConnectionMultiplexers.ContainsKey(connectionString))
                    {
                        return SubscriptionConnectionMultiplexers.GetOrAdd(connectionString, ConnectionMultiplexer.Connect(connectionString));
                    }
                    else
                    {
                        return SubscriptionConnectionMultiplexers[connectionString];
                    }
                }
            }
            else
            {
                return SubscriptionConnectionMultiplexers[connectionString];
            }
        }
    }
}
