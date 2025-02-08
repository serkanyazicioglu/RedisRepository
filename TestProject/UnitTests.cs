using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestProject.Repositories;

namespace TestProject
{
    [TestClass]
    public class UnitTests
    {
        [TestMethod]
        public void TestAll()
        {
            string createdId = string.Empty;

            //New Entity
            using (MemberRepository memberRepository = new())
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
            using (MemberRepository memberRepository = new())
            {
                var members = memberRepository.GetAll("member*");

                foreach (var member in members)
                {
                    member.Title += " Lastname";
                }

                memberRepository.Save();
            }

            //Update Single Entity
            using (MemberRepository memberRepository = new())
            {
                var member = memberRepository.GetById(createdId);

                if (member != null)
                {
                    member.Title = "Selected Member";
                    memberRepository.Save(expiration: TimeSpan.FromDays(2)); //overwrite expiration during save if you like otherwise it uses repository expiration value.
                }
            }

            //IsNew
            using (MemberRepository memberRepository = new())
            {
                var member = memberRepository.CreateNew();
                Console.WriteLine("Is my entity new? Answer: " + memberRepository.IsNew(member));
            }


            //Has Changes
            using (MemberRepository memberRepository = new())
            {
                var member = memberRepository.GetById(createdId);

                if (member != null)
                {
                    member.Title += " and again changed";
                    Console.WriteLine("Has my entity changed? Answer: " + memberRepository.HasChanges(member));
                }
            }

            //Delete Entity
            using (MemberRepository memberRepository = new())
            {
                memberRepository.Delete(createdId);
            }

            //Pub Sub. Use this only when you enable keyspace on Redis. Sample cli code: config set notify-keyspace-events KEA
            //using (MemberRepository memberRepository = new())
            //{
            //    memberRepository.SubscriptionTriggered += MemberRepository_SubscriptionTriggered;
            //}
        }

        private static void MemberRepository_SubscriptionTriggered(object sender, Member entity)
        {
            Console.WriteLine("Entity has changes! Id: " + entity.Id.ToString());
        }
    }
}
