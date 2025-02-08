﻿using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Nhea.Data.Repository.RedisRepository
{
    internal static class RedisConnectionPoolCollection
    {
        internal static ConcurrentDictionary<string, RedisConnectionPool> ConnectionPools { get; set; }

        static RedisConnectionPoolCollection()
        {
            ConnectionPools = new ConcurrentDictionary<string, RedisConnectionPool>();
        }

        private static readonly Lock LockObject = new();

        internal static RedisConnectionPool GetConnectionPool(string connectionString, int poolSize, ConnectionVotingTypes connectionVotingType, Func<Lazy<ConnectionMultiplexer>, object> poolFilter)
        {
            if (!ConnectionPools.ContainsKey(connectionString))
            {
                lock (LockObject)
                {
                    if (!ConnectionPools.ContainsKey(connectionString))
                    {
                        return ConnectionPools.GetOrAdd(connectionString, new RedisConnectionPool(connectionString, poolSize, connectionVotingType, poolFilter));
                    }
                    else
                    {
                        return ConnectionPools[connectionString];
                    }
                }
            }
            else
            {
                return ConnectionPools[connectionString];
            }
        }
    }
}
