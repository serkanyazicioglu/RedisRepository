using Nhea.Data;
using Nhea.Data.Repository.RedisRepository;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace TestProject.Repositories
{
    public partial class Member : RedisDocument
    {
        private string id;
        public override string Id
        {
            get
            {
                if (id == null)
                {
                    id = this.BaseKey + ":" + this.MemberId;
                }

                return id;
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

        public override TimeSpan Expiration => TimeSpan.FromDays(1);
    }
}
