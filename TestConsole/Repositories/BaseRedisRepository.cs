using Nhea.Data.Repository.RedisRepository;

namespace TestConsole.Repositories
{
    public abstract class BaseRedisRepository<T> : Nhea.Data.Repository.RedisRepository.BaseRedisRepository<T>
         where T : RedisDocument, new()
    {
        public override string ConnectionString => "defaultDatabase=0,localhost:6379,ssl=false,abortConnect=true,connectTimeout=10000,connectRetry=10";
    }
}
