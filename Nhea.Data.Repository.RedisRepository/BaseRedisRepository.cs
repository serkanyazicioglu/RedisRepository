using Newtonsoft.Json;
using Nhea.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

namespace Nhea.Data.Repository.RedisRepository
{
    public abstract class BaseRedisRepository<T> : IDisposable where T : RedisDocument, new()
    {
        public abstract string ConnectionString { get; }

        private static object connectLockObject = new object();

        private static ConnectionMultiplexer connection = null;
        public ConnectionMultiplexer Connection
        {
            get
            {
                if (connection == null)
                {
                    lock (connectLockObject)
                    {
                        if (connection == null)
                        {
                            connection = ConnectionMultiplexer.Connect(ConnectionString);
                        }
                    }
                }

                return connection;
            }
        }

        private static object databaseLockObject = new object();

        public static IDatabase currentDatabase = null;
        public IDatabase CurrentDatabase
        {
            get
            {
                if (currentDatabase == null)
                {
                    lock (databaseLockObject)
                    {
                        if (currentDatabase == null)
                        {
                            currentDatabase = Connection.GetDatabase();
                        }
                    }
                }

                return currentDatabase;
            }
        }

        private static object serverLockObject = new object();

        public static IServer currentServer = null;
        public IServer CurrentServer
        {
            get
            {
                if (currentServer == null)
                {
                    lock (serverLockObject)
                    {
                        if (currentServer == null)
                        {
                            currentServer = Connection.GetServer(Connection.GetEndPoints().First());
                        }
                    }
                }

                return currentServer;
            }
        }

        private static ISubscriber currentSubscriber = null;
        public ISubscriber CurrentSubscriber
        {
            get
            {
                if (currentSubscriber == null)
                {
                    currentSubscriber = Connection.GetSubscriber();
                }

                return currentSubscriber;
            }
        }

        private int? defaultDatabase = null;
        public int DefaultDatabase
        {
            get
            {
                if (defaultDatabase == null)
                {
                    var connectionStringValues = ConnectionString.Split(',');

                    foreach (var connectionStringVal in connectionStringValues)
                    {
                        if (connectionStringVal.StartsWith("defaultDatabase"))
                        {
                            defaultDatabase = Convert.ToInt32(connectionStringVal.Replace("defaultDatabase=", String.Empty));
                            break;
                        }
                    }

                    if (defaultDatabase == null)
                    {
                        defaultDatabase = 0;
                    }
                }

                return defaultDatabase.Value;
            }
        }

        private List<T> Items = new List<T>();

        protected virtual bool EnableCaching => true;

        protected virtual bool PreventSubForAlreadyCachedData => true;

        private static object cacheLockObject = new object();

        private static MemoryCache currentMemoryCache = null;
        private static MemoryCache CurrentMemoryCache
        {
            get
            {
                if (currentMemoryCache == null)
                {
                    lock (cacheLockObject)
                    {
                        if (currentMemoryCache == null)
                        {
                            currentMemoryCache = new MemoryCache("RedisDataCache");
                        }
                    }
                }

                return currentMemoryCache;
            }
        }

        private bool SetCachedEntity(T entity)
        {
            if (EnableCaching)
            {
                var cachedData = GetCachedEntity(entity.Id);

                if (cachedData == null || cachedData.ModifyDate < entity.ModifyDate)
                {
                    CurrentMemoryCache.Set(entity.Id, entity, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(30) });
                    return true;
                }
            }

            return false;
        }

        private void SetCachedEntity(string key, object value)
        {
            if (EnableCaching)
            {
                CurrentMemoryCache.Set(key, value, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(30) });
            }
        }

        private T GetCachedEntity(string key)
        {
            if (EnableCaching)
            {
                if (CurrentMemoryCache.Contains(key))
                {
                    var cachedData = CurrentMemoryCache[key];

                    if (cachedData != null && cachedData.ToString() != String.Empty)
                    {
                        return cachedData as T;
                    }
                }
            }

            return null;
        }

        private void DeleteCachedEntity(string key)
        {
            if (EnableCaching)
            {
                CurrentMemoryCache.Remove(key);
            }
        }

        private Dictionary<string, string> DirtyCheckItems = new Dictionary<string, string>();

        public T CreateNew()
        {
            var entity = new T();
            entity.CreateDate = DateTime.Now;

            lock (lockObject)
            {
                Items.Add(entity);
            }

            return entity;
        }

        public void Add(T entity)
        {
            AddCore(entity, true);
        }

        public void Add(List<T> entities)
        {
            foreach (var entity in entities)
            {
                AddCore(entity, true);
            }
        }

        private object lockObject = new object();

        private void AddCore(T entity, bool isNew)
        {
            lock (lockObject)
            {
                if (entity != null)
                {
                    if (Items.Any(query => query.Id == entity.Id))
                    {
                        Items.RemoveAll(query => query.Id == entity.Id);
                    }

                    Items.Add(entity);

                    if (!isNew)
                    {
                        if (EnableCaching)
                        {
                            SetCachedEntity(entity);
                        }

                        if (!DirtyCheckItems.ContainsKey(entity.Id))
                        {
                            DirtyCheckItems.Add(entity.Id, JsonConvert.SerializeObject(entity));
                        }
                    }
                }
            }
        }

        public void Remove(T entity)
        {
            lock (lockObject)
            {
                if (entity != null)
                {
                    Items.RemoveAll(query => query.Id == entity.Id);
                }
            }
        }

        private static ConcurrentDictionary<string, object> LockObjects = new ConcurrentDictionary<string, object>();

        private T GetFromCacheSafely(string id)
        {
            if (EnableCaching)
            {
                var cachedEntity = GetCachedEntity(id);

                if (cachedEntity != null)
                {
                    AddCore(cachedEntity, false);

                    return cachedEntity;
                }
            }

            return null;
        }

        public T GetById(string id)
        {
            var baseKey = Activator.CreateInstance<T>().BaseKey;

            if (!id.StartsWith(baseKey))
            {
                id = baseKey + id;
            }

            var cachedEntity = GetFromCacheSafely(id);

            if (cachedEntity != null)
            {
                return cachedEntity;
            }

            var lockObject = LockObjects.GetOrAdd(id, new object());

            lock (lockObject)
            {
                cachedEntity = GetFromCacheSafely(id);

                if (cachedEntity != null)
                {
                    return cachedEntity;
                }

                var entity = GetByIdCore(id);

                if (entity != null)
                {
                    AddCore(entity, false);
                }

                return entity;
            }
        }

        private T GetByIdCore(string id)
        {
            var currentValue = CurrentDatabase.StringGet(id);

            if (currentValue.IsNullOrEmpty)
            {
                return null;
            }
            else
            {
                return JsonConvert.DeserializeObject<T>(currentValue);
            }
        }

        /// <summary>
        /// Fetchs items with specific identities.
        /// </summary>
        /// <param name="ids">List of identities. Identities shouldn't contain *.</param>
        /// <returns></returns>
        public List<T> GetAll(List<string> ids)
        {
            List<RedisKey> redisKeys = new List<RedisKey>();

            var returnData = new List<T>();

            var baseKey = Activator.CreateInstance<T>().BaseKey;

            foreach (var key in ids)
            {
                string redisKey = key;

                if (!key.StartsWith(baseKey))
                {
                    redisKey = baseKey + key;
                }

                if (EnableCaching && CurrentMemoryCache.Contains(redisKey))
                {
                    var cachedEntity = GetCachedEntity(redisKey);

                    if (cachedEntity != null)
                    {
                        AddCore(cachedEntity, false);

                        returnData.Add(cachedEntity);
                    }
                }
                else
                {
                    redisKeys.Add(redisKey);
                }
            }

            if (redisKeys.Any())
            {
                List<T> items = new List<T>();

                foreach (var redisKey in redisKeys)
                {
                    var entity = GetByIdCore(redisKey);

                    if (entity != null)
                    {
                        AddCore(entity, false);

                        returnData.Add(entity);
                    }
                }

                foreach (var redisKey in redisKeys)
                {
                    if (!returnData.Any(query => query.Id == redisKey))
                    {
                        SetCachedEntity(redisKey, String.Empty);
                    }
                }
            }

            return returnData;
        }

        /// <summary>
        /// Scans and retrieves items with a specific matching pattern.
        /// </summary>
        /// <param name="pattern">A pattern like basekey:*</param>
        /// <param name="count">Fetch limit</param>
        /// <returns></returns>
        public List<T> GetAll(string pattern, int count = 10000)
        {
            var listOfKeys = Scan(pattern, count);

            return GetAll(listOfKeys);
        }

        /// <summary>
        /// Scans database with a specific pattern.
        /// </summary>
        /// <param name="pattern">A pattern like basekey:*</param>
        /// <param name="count">Fetch limit</param>
        /// <returns>List of identities</returns>
        public List<string> Scan(string pattern, int count = 10000)
        {
            var baseKey = Activator.CreateInstance<T>().BaseKey;

            if (!pattern.StartsWith(baseKey))
            {
                pattern = baseKey + pattern;
            }

            List<string> listOfKeys = new List<string>();

            var keysResult = CurrentServer.Keys(DefaultDatabase, pattern, count, CommandFlags.None);

            foreach (var key in keysResult)
            {
                listOfKeys.Add(key.ToString());
            }

            //int nextCursor = 0;

            //do
            //{
            //    var redisResult = CurrentDatabase.Execute("SCAN", new object[] { nextCursor.ToString(), "MATCH", pattern, "COUNT", count.ToString() });
            //    var innerResult = (RedisResult[])redisResult;

            //    nextCursor = int.Parse((string)innerResult[0]);

            //    List<string> resultLines = ((string[])innerResult[1]).ToList();
            //    listOfKeys.AddRange(resultLines);
            //}
            //while (nextCursor != 0);

            return listOfKeys;
        }

        public void Dispose()
        {
            Items = null;
            DirtyCheckItems = null;

            foreach (var subscription in Subscriptions)
            {
                Unsubscribe(subscription);
            }
        }

        public bool IsNew(T entity)
        {
            return !entity.ModifyDate.HasValue;
        }

        public void Delete(T entity)
        {
            CurrentDatabase.KeyDelete(entity.Id, flags: CommandFlags.FireAndForget);
            DeleteCachedEntity(entity.Id);
        }

        public void Delete(string id)
        {
            CurrentDatabase.KeyDelete(id, flags: CommandFlags.FireAndForget);
            DeleteCachedEntity(id);
        }

        public virtual TimeSpan Expiration => TimeSpan.FromDays(15);

        public bool HasChanges(T entity)
        {
            if (DirtyCheckItems.ContainsKey(entity.Id))
            {
                var newItem = JsonConvert.SerializeObject(entity);

                return newItem != DirtyCheckItems[entity.Id];
            }

            return true;
        }

        public void Save(bool forceUpdate = false, TimeSpan? expiration = null)
        {
            if (!expiration.HasValue)
            {
                expiration = Expiration;
            }

            var savingList = Items.ToList();

            for (int i = 0; i < savingList.Count(); i++)
            {
                var item = savingList[i];

                if (forceUpdate || HasChanges(item))
                {
                    item.ModifyDate = DateTime.Now;

                    if (EnableCaching)
                    {
                        var cachedItem = GetCachedEntity(item.Id);

                        if (cachedItem != null)
                        {
                            cachedItem.ModifyDate = item.ModifyDate;
                        }
                    }

                    var newValue = JsonConvert.SerializeObject(item);

                    CurrentDatabase.StringSet(item.Id, newValue, expiration.Value, flags: CommandFlags.FireAndForget);

                    if (DirtyCheckItems.ContainsKey(item.Id))
                    {
                        DirtyCheckItems.Remove(item.Id);
                    }

                    DirtyCheckItems.Add(item.Id, newValue);
                }
            }
        }

        private List<string> Subscriptions = new List<string>();

        public delegate void SubscriptionTriggeredEventHandler(object sender, T entity);
        public event SubscriptionTriggeredEventHandler SubscriptionTriggered;

        public void Subscribe(string pattern)
        {
            var baseKey = Activator.CreateInstance<T>().BaseKey;

            if (!pattern.StartsWith(baseKey))
            {
                pattern = baseKey + pattern;
            }

            if (!Subscriptions.Contains(pattern))
            {
                CurrentSubscriber.Subscribe("__keyspace@" + DefaultDatabase + "__:" + pattern, SubscriptionTriggeredResponse, CommandFlags.FireAndForget);

                Subscriptions.Add(pattern);
            }
        }

        public void Unsubscribe(string pattern)
        {
            var baseKey = Activator.CreateInstance<T>().BaseKey;

            if (!pattern.StartsWith(baseKey))
            {
                pattern = baseKey + pattern;
            }

            CurrentSubscriber.Unsubscribe("__keyspace@" + DefaultDatabase + "__:" + pattern, SubscriptionTriggeredResponse, CommandFlags.FireAndForget);

            Subscriptions.Remove(pattern);
        }

        static BaseRedisRepository()
        {
            if (!SubscriptionRepositories.DisabledAutoSubscriptionTypes.Contains(typeof(T)))
            {
                CurrentSubscribingRepository = GetSubscribingRepository();
            }
        }

        private static BaseRedisRepository<T> CurrentSubscribingRepository = null;

        private static object subscriberLockObject = new object();

        public static BaseRedisRepository<T> GetSubscribingRepository()
        {
            if (CurrentSubscribingRepository == null)
            {
                lock (subscriberLockObject)
                {
                    if (CurrentSubscribingRepository == null)
                    {
                        var currentDocumentType = typeof(T);
                        var currentAssembly = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic)
                            .Single(query => query.FullName == currentDocumentType.Assembly.FullName);

                        foreach (Type exportedType in currentAssembly.GetExportedTypes().Where(query => query.BaseType != null && query.BaseType.UnderlyingSystemType != null && query.BaseType.UnderlyingSystemType.IsGenericType))
                        {
                            if (exportedType.BaseType.UnderlyingSystemType.GetGenericArguments().FirstOrDefault() == currentDocumentType)
                            {
                                CurrentSubscribingRepository = Activator.CreateInstance(exportedType) as BaseRedisRepository<T>;

                                if (CurrentSubscribingRepository.EnableCaching)
                                {
                                    CurrentSubscribingRepository.Subscribe("*");
                                }

                                break;
                            }
                        }
                    }
                }
            }

            return CurrentSubscribingRepository;
        }

        private void SubscriptionTriggeredResponse(RedisChannel redisChannel, RedisValue redisValue)
        {
            try
            {
                if (redisValue.ToString() == "set")
                {
                    string key = redisChannel.ToString().Replace("__keyspace@" + DefaultDatabase + "__:", String.Empty);
                    var currentData = GetByIdCore(key);

                    if (EnableCaching)
                    {
                        if (!SetCachedEntity(currentData))
                        {
                            if (PreventSubForAlreadyCachedData)
                            {
                                return;
                            }
                        }
                    }

                    if (SubscriptionTriggered == null)
                    {
                        return;
                    }

                    var receivers = SubscriptionTriggered.GetInvocationList();
                    foreach (SubscriptionTriggeredEventHandler receiver in receivers)
                    {
                        receiver.BeginInvoke(this, currentData, null, null);
                    }
                }
                else if (redisValue.ToString() == "del")
                {
                    try
                    {
                        string key = redisChannel.ToString().Replace("__keyspace@" + DefaultDatabase + "__:", String.Empty);
                        DeleteCachedEntity(key);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
    }

    public static class SubscriptionRepositories
    {
        public static List<Type> DisabledAutoSubscriptionTypes = new List<Type>();
    }
}
