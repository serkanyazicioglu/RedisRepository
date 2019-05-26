using Nhea.Data.Repository.RedisRepository;
using System;
using System.Collections.Generic;
using System.Text;

namespace SampleApp.Repositories
{
    public abstract class BaseRedisRepository<T> : Nhea.Data.Repository.RedisRepository.BaseRedisRepository<T>
         where T : RedisDocument, new()
    {
        public override string ConnectionString => "defaultDatabase=0,<hostname>:<port>,password=<AAAAAAAAAAAAAAAWBBBBBBBBBBBBBBBBBBBB=>,ssl=true,abortConnect=false,connectTimeout=10000,connectRetry=10";
    }
}
