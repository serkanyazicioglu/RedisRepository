using SampleApp.Repositories;
using System;

namespace SampleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello Redis!");

            string createdId = string.Empty;

            //New Entity
            using (MemberRepository memberRepository = new MemberRepository())
            {
                var member = memberRepository.CreateNew();

                member.MemberId = Guid.NewGuid().ToString();
                member.Title = "Test Member";
                member.UserName = "username";
                member.Password = "password";
                member.Email = "test@test.com";
                memberRepository.Save();

                createdId = member.Id;
            }

            //Update Multiple Entity
            using (MemberRepository memberRepository = new MemberRepository())
            {
                var members = memberRepository.GetAll("member*");

                foreach (var member in members)
                {
                    member.Title += " Lastname";
                }

                memberRepository.Save();
            }

            //Update Single Entity
            using (MemberRepository memberRepository = new MemberRepository())
            {
                var member = memberRepository.GetById(createdId);

                if (member != null)
                {
                    member.Title = "Selected Member";
                    memberRepository.Save(expiration: TimeSpan.FromDays(2)); //overwrite expiration during save if you like otherwise it uses repository expiration value.
                }
            }

            //IsNew
            using (MemberRepository memberRepository = new MemberRepository())
            {
                var member = memberRepository.CreateNew();
                Console.WriteLine("Is my entity new? Answer: " + memberRepository.IsNew(member));
            }


            //Has Changes
            using (MemberRepository memberRepository = new MemberRepository())
            {
                var member = memberRepository.GetById(createdId);

                if (member != null)
                {
                    member.Title += " and again changed";
                    Console.WriteLine("Has my entity changed? Answer: " + memberRepository.HasChanges(member));
                }
            }

            //Delete Entity
            using (MemberRepository memberRepository = new MemberRepository())
            {
                memberRepository.Delete(createdId);
            }

            //Pub Sub. Use this only when you enable keyspace on Redis. Sample cli code: config set notify-keyspace-events KEA
            //using (MemberRepository memberRepository = new MemberRepository())
            //{
            //    memberRepository.SubscriptionTriggered += MemberRepository_SubscriptionTriggered;
            //}

            Console.WriteLine("Job done!");
            Console.ReadLine();
        }

        private static void MemberRepository_SubscriptionTriggered(object sender, Member entity)
        {
            Console.WriteLine("Entity has changes! Id: " + entity.Id.ToString());
        }
    }
}
