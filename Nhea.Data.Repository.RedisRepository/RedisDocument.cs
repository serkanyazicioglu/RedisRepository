using Newtonsoft.Json;
using System;

namespace Nhea.Data.Repository.RedisRepository
{
    public abstract class RedisDocument
    {
        public abstract string Id { get; set; }

        [JsonIgnore]
        public abstract string BaseKey { get; }

        public DateTime CreateDate { get; set; }

        public DateTime? ModifyDate { get; set; }
    }
}
