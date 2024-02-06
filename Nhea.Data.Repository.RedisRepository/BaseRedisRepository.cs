﻿using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace Nhea.Data.Repository.RedisRepository
{
    public abstract class BaseRedisRepository<T> : IDisposable where T : RedisDocument, new()
    {
        public abstract string ConnectionString { get; }

        public virtual CommandFlags SaveCommandFlags => CommandFlags.None;

        public virtual ConnectionTypes ConnectionType => ConnectionTypes.Default;

        public virtual ConnectionVotingTypes ConnectionVotingType => ConnectionVotingTypes.LeastLoaded;

        public virtual Func<Lazy<ConnectionMultiplexer>, object> CustomConnectionVotingFilter => null;

        public virtual int PoolSize => 5;

        protected virtual System.Text.Json.JsonSerializerOptions JsonSerializerOptions => null;

        public virtual TimeSpan CacheExpiration => TimeSpan.FromMinutes(10);

        Lazy<ConnectionMultiplexer> lazyConnection = null;

        public virtual ConnectionMultiplexer Connection
        {
            get
            {
                if (this.ConnectionType == ConnectionTypes.Default)
                {
                    return ConnectionMultiplexerCollection.GetConnection(ConnectionString);
                }
                else if (this.ConnectionType == ConnectionTypes.Lazy)
                {
                    if (lazyConnection == null)
                    {
                        lazyConnection = new Lazy<ConnectionMultiplexer>(() => { return ConnectionMultiplexer.Connect(ConnectionString); });
                    }

                    return lazyConnection.Value;
                }
                else if (this.ConnectionType == ConnectionTypes.ConnectionPool)
                {
                    return RedisConnectionPoolCollection.GetConnectionPool(ConnectionString, PoolSize, ConnectionVotingType, CustomConnectionVotingFilter).Connection;
                }

                return null;
            }
        }

        public IDatabase CurrentDatabase
        {
            get
            {
                return Connection.GetDatabase();
            }
        }

        public IServer CurrentServer
        {
            get
            {
                return Connection.GetServer(Connection.GetEndPoints().First());
            }
        }

        public ConnectionMultiplexer SubscriptionConnection
        {
            get
            {
                return ConnectionMultiplexerCollection.GetSubscriptionConnection(ConnectionString);
            }
        }

        private static ISubscriber currentSubscriber = null;
        public ISubscriber CurrentSubscriber
        {
            get
            {
                if (currentSubscriber == null)
                {
                    currentSubscriber = SubscriptionConnection.GetSubscriber();
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

        protected virtual bool EnableCaching => false;
        protected virtual SubscriptionTypes CachingSubscriptionType => SubscriptionTypes.Keyspace;

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
                    CurrentMemoryCache.Set(entity.Id, entity, new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.Add(CacheExpiration) });
                    return true;
                }
            }

            return false;
        }

        private void SetCachedEntity(string key, object value)
        {
            if (EnableCaching)
            {
                CurrentMemoryCache.Set(key, value, new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.Add(CacheExpiration) });
            }
        }

        private T GetCachedEntity(string key)
        {
            if (EnableCaching)
            {
                var cachedData = CurrentMemoryCache.Get(key);

                if (cachedData != null && cachedData.ToString() != String.Empty)
                {
                    return cachedData as T;
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

        private List<T> Items = new List<T>();

        private Dictionary<string, string> DirtyCheckItems = new Dictionary<string, string>();

        public T CreateNew()
        {
            var entity = new T();
            entity.CreateDate = DateTime.UtcNow;

            Items.Add(entity);

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

        private void AddCore(T entity, bool isNew)
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
                        DirtyCheckItems.Add(entity.Id, System.Text.Json.JsonSerializer.Serialize(entity));
                    }
                }
            }
        }

        public void Remove(T entity)
        {
            if (entity != null)
            {
                Items.RemoveAll(query => query.Id == entity.Id);
            }
        }

        private string baseKey = null;
        private string GetBaseKey()
        {
            if (baseKey == null)
            {
                baseKey = Activator.CreateInstance<T>().BaseKey;
            }

            return baseKey;
        }

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
            var currentBaseKey = GetBaseKey();

            if (!id.StartsWith(currentBaseKey))
            {
                id = currentBaseKey + id;
            }

            var cachedEntity = GetFromCacheSafely(id);

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

        public async Task<T> GetByIdAsync(string id)
        {
            var currentBaseKey = GetBaseKey();

            if (!id.StartsWith(currentBaseKey))
            {
                id = currentBaseKey + id;
            }

            var cachedEntity = GetFromCacheSafely(id);

            if (cachedEntity != null)
            {
                return cachedEntity;
            }

            var entity = await GetByIdCoreAsync(id);

            if (entity != null)
            {
                AddCore(entity, false);
            }

            return entity;
        }

        private T GetByIdCore(string id)
        {
            int tryCount = 0;

            while (true)
            {
                try
                {
                    return ReturnRedisValue(CurrentDatabase.StringGet(id));
                }
                catch (Exception ex)
                {
                    tryCount++;

                    Task.Delay(5 * tryCount).ConfigureAwait(false).GetAwaiter().GetResult();

                    if (tryCount > 5)
                    {
                        ex.Data.Add("Id", id);
                        throw;
                    }
                }
            }
        }

        private async Task<T> GetByIdCoreAsync(string id)
        {
            int tryCount = 0;

            while (true)
            {
                try
                {
                    return ReturnRedisValue(await CurrentDatabase.StringGetAsync(id));
                }
                catch (Exception ex)
                {
                    tryCount++;

                    await Task.Delay(5 * tryCount);

                    if (tryCount > 5)
                    {
                        ex.Data.Add("Id", id);
                        throw;
                    }
                }
            }
        }

        private T ReturnRedisValue(RedisValue currentValue)
        {
            if (currentValue.IsNullOrEmpty)
            {
                return null;
            }
            else
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(currentValue);
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

            var currentBaseKey = GetBaseKey();

            foreach (var key in ids)
            {
                string redisKey = key;

                if (!key.StartsWith(currentBaseKey))
                {
                    redisKey = currentBaseKey + key;
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
        /// Fetchs items with specific identities.
        /// </summary>
        /// <param name="ids">List of identities. Identities shouldn't contain *.</param>
        /// <returns></returns>
        public async Task<List<T>> GetAllAsync(List<string> ids)
        {
            List<RedisKey> redisKeys = new List<RedisKey>();

            var returnData = new List<T>();

            var currentBaseKey = GetBaseKey();

            foreach (var key in ids)
            {
                string redisKey = key;

                if (!key.StartsWith(currentBaseKey))
                {
                    redisKey = currentBaseKey + key;
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
                foreach (var redisKey in redisKeys)
                {
                    var entity = await GetByIdCoreAsync(redisKey);

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

        public async Task<List<T>> GetAllAsync(string pattern, int count = 10000)
        {
            var listOfKeys = await ScanAsync(pattern, count);

            return await GetAllAsync(listOfKeys);
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
            var currentBaseKey = GetBaseKey();

            if (!pattern.StartsWith(currentBaseKey))
            {
                pattern = currentBaseKey + pattern;
            }

            List<string> listOfKeys = new List<string>();

            var keysResult = CurrentServer.Keys(DefaultDatabase, pattern, count, CommandFlags.None);

            foreach (var key in keysResult)
            {
                listOfKeys.Add(key.ToString());
            }

            return listOfKeys;
        }

        public async Task<List<string>> ScanAsync(string pattern, int count = 10000)
        {
            var currentBaseKey = GetBaseKey();

            if (!pattern.StartsWith(currentBaseKey))
            {
                pattern = currentBaseKey + pattern;
            }

            List<string> listOfKeys = new List<string>();

            var keysResult = CurrentServer.KeysAsync(DefaultDatabase, pattern, count);

            await foreach (var key in keysResult)
            {
                listOfKeys.Add(key.ToString());
            }

            return listOfKeys;
        }

        public void Dispose()
        {
            Items = null;
            DirtyCheckItems = null;

            try
            {
                foreach (var subscription in Subscriptions)
                {
                    Unsubscribe(subscription);
                }
            }
            catch
            {
            }
        }

        public bool IsNew(T entity)
        {
            return !entity.ModifyDate.HasValue;
        }

        public void Delete(T entity)
        {
            Delete(entity.Id);
        }

        public void Delete(string id)
        {
            CurrentDatabase.KeyDelete(id, flags: CommandFlags.FireAndForget);
            DeleteCachedEntity(id);
        }

        public async Task DeleteAsync(T entity)
        {
            await DeleteAsync(entity.Id);
        }

        public async Task DeleteAsync(string id)
        {
            await CurrentDatabase.KeyDeleteAsync(id, flags: CommandFlags.FireAndForget);
            DeleteCachedEntity(id);
        }

        public virtual TimeSpan Expiration => TimeSpan.FromDays(15);

        public bool HasChanges(T entity)
        {
            if (DirtyCheckItems.TryGetValue(entity.Id, out var dirtyItem))
            {
                var newItem = System.Text.Json.JsonSerializer.Serialize(entity);

                return newItem != dirtyItem;
            }

            return true;
        }

        public void Save()
        {
            Save(false, null, publish: false);
        }

        public void Save(bool forceUpdate)
        {
            Save(forceUpdate, null, publish: false);
        }

        public void Save(TimeSpan? expiration)
        {
            Save(false, expiration, publish: false);
        }

        public void Save(bool forceUpdate = false, TimeSpan? expiration = null)
        {
            Save(forceUpdate, expiration, publish: false);
        }

        public void Save(bool forceUpdate = false, TimeSpan? expiration = null, bool publish = false)
        {
            if (!expiration.HasValue)
            {
                expiration = Expiration;
            }

            var savingList = Items.ToList();

            for (int i = 0; i < savingList.Count; i++)
            {
                var item = savingList[i];

                if (forceUpdate || HasChanges(item))
                {
                    item.ModifyDate = DateTime.UtcNow;

                    if (EnableCaching)
                    {
                        var cachedItem = GetCachedEntity(item.Id);

                        if (cachedItem != null)
                        {
                            cachedItem.ModifyDate = item.ModifyDate;
                        }
                    }

                    var newValue = System.Text.Json.JsonSerializer.Serialize(item, JsonSerializerOptions);

                    CurrentDatabase.StringSet(item.Id, newValue, expiration.Value, flags: SaveCommandFlags);

                    if (publish)
                    {
                        CurrentDatabase.Publish(item.Id, newValue);
                    }

                    DirtyCheckItems[item.Id] = newValue;
                }
            }
        }

        public async Task SaveAsync()
        {
            await SaveAsync(false, null, false);
        }

        public async Task SaveAsync(bool forceUpdate)
        {
            await SaveAsync(forceUpdate, null, publish: false);
        }

        public async Task SaveAsync(TimeSpan? expiration)
        {
            await SaveAsync(false, expiration, publish: false);
        }

        public async Task SaveAsync(bool forceUpdate = false, TimeSpan? expiration = null)
        {
            await SaveAsync(forceUpdate, expiration, publish: false);
        }

        public async Task SaveAsync(bool forceUpdate = false, TimeSpan? expiration = null, bool publish = false)
        {
            if (!expiration.HasValue)
            {
                expiration = Expiration;
            }

            var savingList = Items.ToList();

            for (int i = 0; i < savingList.Count; i++)
            {
                var item = savingList[i];

                if (forceUpdate || HasChanges(item))
                {
                    item.ModifyDate = DateTime.UtcNow;

                    if (EnableCaching)
                    {
                        var cachedItem = GetCachedEntity(item.Id);

                        if (cachedItem != null)
                        {
                            cachedItem.ModifyDate = item.ModifyDate;
                        }
                    }

                    var newValue = System.Text.Json.JsonSerializer.Serialize(item, JsonSerializerOptions);

                    await CurrentDatabase.StringSetAsync(item.Id, newValue, expiration.Value, flags: SaveCommandFlags);

                    if (publish)
                    {
                        await CurrentDatabase.PublishAsync(item.Id, newValue);
                    }

                    DirtyCheckItems[item.Id] = newValue;
                }
            }
        }

        public long Publish(string key, string value)
        {
            return CurrentDatabase.Publish(key, value);
        }

        public long Publish(T entity)
        {
            return CurrentDatabase.Publish(entity.Id, System.Text.Json.JsonSerializer.Serialize(entity, JsonSerializerOptions));
        }

        public async Task<long> PublishAsync(string key, string value)
        {
            return await CurrentDatabase.PublishAsync(key, value);
        }

        public async Task<long> PublishAsync(T entity)
        {
            return await CurrentDatabase.PublishAsync(entity.Id, System.Text.Json.JsonSerializer.Serialize(entity, JsonSerializerOptions));
        }

        private readonly List<string> Subscriptions = new List<string>();

        public delegate void SubscriptionTriggeredEventHandler(object sender, T entity);
        public event SubscriptionTriggeredEventHandler SubscriptionTriggered;

        public delegate void CachedChangedEventHandler(object sender, T entity);
        public event CachedChangedEventHandler CacheChanged;

        public void Subscribe(string pattern)
        {
            Subscribe(pattern, subscriptionType: SubscriptionTypes.Keyspace);
        }

        public void Subscribe(string pattern, SubscriptionTypes subscriptionType = SubscriptionTypes.Keyspace)
        {
            var currentBaseKey = GetBaseKey();

            if (!pattern.StartsWith(currentBaseKey))
            {
                pattern = currentBaseKey + pattern;
            }

            if (!Subscriptions.Contains(pattern))
            {
                if (subscriptionType == SubscriptionTypes.Keyspace)
                {
                    pattern = "__keyspace@" + DefaultDatabase + "__:" + pattern;
                }

                CurrentSubscriber.Subscribe(pattern, SubscriptionTriggeredResponse, CommandFlags.FireAndForget);

                Subscriptions.Add(pattern);
            }
        }

        public async Task SubscribeAsync(string pattern)
        {
            await SubscribeAsync(pattern, subscriptionType: SubscriptionTypes.Keyspace);
        }

        public async Task SubscribeAsync(string pattern, SubscriptionTypes subscriptionType = SubscriptionTypes.Keyspace)
        {
            var currentBaseKey = GetBaseKey();

            if (!pattern.StartsWith(currentBaseKey))
            {
                pattern = currentBaseKey + pattern;
            }

            if (!Subscriptions.Contains(pattern))
            {
                if (subscriptionType == SubscriptionTypes.Keyspace)
                {
                    pattern = "__keyspace@" + DefaultDatabase + "__:" + pattern;
                }

                await CurrentSubscriber.SubscribeAsync(pattern, SubscriptionTriggeredResponse, CommandFlags.FireAndForget);

                Subscriptions.Add(pattern);
            }
        }

        public void Unsubscribe(string pattern)
        {
            Unsubscribe(pattern, subscriptionType: SubscriptionTypes.Keyspace);
        }

        public void Unsubscribe(string pattern, SubscriptionTypes subscriptionType = SubscriptionTypes.Keyspace)
        {
            var currentBaseKey = GetBaseKey();

            if (!pattern.StartsWith(currentBaseKey))
            {
                pattern = currentBaseKey + pattern;
            }

            if (subscriptionType == SubscriptionTypes.Keyspace)
            {
                pattern = "__keyspace@" + DefaultDatabase + "__:" + pattern;
            }

            CurrentSubscriber.Unsubscribe(pattern, SubscriptionTriggeredResponse, CommandFlags.FireAndForget);

            Subscriptions.Remove(pattern);
        }

        public async Task UnsubscribeAsync(string pattern)
        {
            await UnsubscribeAsync(pattern, subscriptionType: SubscriptionTypes.Keyspace);
        }

        public async Task UnsubscribeAsync(string pattern, SubscriptionTypes subscriptionType = SubscriptionTypes.Keyspace)
        {
            var currentBaseKey = GetBaseKey();

            if (!pattern.StartsWith(currentBaseKey))
            {
                pattern = currentBaseKey + pattern;
            }

            if (subscriptionType == SubscriptionTypes.Keyspace)
            {
                pattern = "__keyspace@" + DefaultDatabase + "__:" + pattern;
            }

            await CurrentSubscriber.UnsubscribeAsync(pattern, SubscriptionTriggeredResponse, CommandFlags.FireAndForget);

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
                                    CurrentSubscribingRepository.Subscribe("*", subscriptionType: CurrentSubscribingRepository.CachingSubscriptionType);
                                }

                                break;
                            }
                        }
                    }
                }
            }

            return CurrentSubscribingRepository;
        }

        private async void SubscriptionTriggeredResponse(RedisChannel redisChannel, RedisValue redisValue)
        {
            try
            {
                string channelString = redisChannel.ToString();

                if (channelString.Contains("__keyspace@"))
                {
                    if (redisValue.ToString() == "set")
                    {
                        string key = channelString.Replace("__keyspace@" + DefaultDatabase + "__:", String.Empty);
                        T currentData = await GetByIdCoreAsync(key);

                        CompleteSubscriptionTrigger(currentData);
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
                else
                {
                    T currentData = System.Text.Json.JsonSerializer.Deserialize<T>(redisValue);
                    CompleteSubscriptionTrigger(currentData);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string key = redisChannel.ToString().Replace("__keyspace@" + DefaultDatabase + "__:", string.Empty);
                    DeleteCachedEntity(key);

                    ex.Data.Add("Key", key);
                }
                catch
                {
                }

                RedisRepositoryErrorManager.LogException(this, ex);
            }
        }

        private void CompleteSubscriptionTrigger(T currentData)
        {
            if (EnableCaching)
            {
                if (SetCachedEntity(currentData))
                {
                    CacheChanged?.Invoke(this, currentData);
                }
                else
                {
                    if (PreventSubForAlreadyCachedData)
                    {
                        return;
                    }
                }
            }

            SubscriptionTriggered?.Invoke(this, currentData);
        }
    }
}
