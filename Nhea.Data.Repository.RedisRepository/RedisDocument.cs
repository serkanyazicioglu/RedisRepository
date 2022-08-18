using System;

namespace Nhea.Data.Repository.RedisRepository
{
    public abstract class RedisDocument
    {
        public abstract string Id { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public abstract string BaseKey { get; }

        public DateTime CreateDate { get; set; }

        public DateTime? ModifyDate { get; set; }
    }
}
