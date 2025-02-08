using Nhea.Data.Repository.RedisRepository;
using System;

namespace TestConsole.Repositories
{
    public partial class Member : RedisDocument
    {
        private string id;
        public override string Id
        {
            get
            {
                return this.BaseKey + ":" + this.MemberId;
            }
            set
            {
                id = value;
            }
        }

        public string MemberId { get; set; }

        public override string BaseKey => "member";

        public string Title { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public int Status { get; set; }

        public string Email { get; set; }
    }


    public class MemberRepository : BaseRedisRepository<Member>
    {
        protected override bool EnableCaching => false;

        public override ConnectionTypes ConnectionType => ConnectionTypes.ConnectionPool;

        public override int PoolSize => 5;

        public override ConnectionVotingTypes ConnectionVotingType => ConnectionVotingTypes.LeastLoaded;

        public override TimeSpan Expiration => TimeSpan.FromDays(1);
    }
}
