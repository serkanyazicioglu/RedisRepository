using Nhea.Data.Repository.RedisRepository;
using System;
using System.Threading.Tasks;
using TestConsole.Repositories;

namespace TestConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            string createdId = string.Empty;

            //New Entity
            using (MemberRepository memberRepository = new())
            {
                memberRepository.SubscriptionTriggered += MemberRepository_SubscriptionTriggered;
                memberRepository.Subscribe("*", SubscriptionTypes.PubSub);

                var member = memberRepository.CreateNew();

                member.MemberId = Guid.NewGuid().ToString();
                member.Title = "Test Member";
                member.UserName = "username";
                member.Password = "password";
                member.Email = "test@test.com";
                memberRepository.Save(publish: true);

                createdId = member.Id;
            }

            RedisRepositoryErrorManager.ErrorOccuredEvent += RedisRepositoryErrorManager_ErrorOccuredEvent;

            using (MemberRepository memberRepository = new())
            {
                memberRepository.SubscriptionTriggered += MemberRepository_SubscriptionTriggered;
                memberRepository.Subscribe("*");

                //Parallel.For(0, 5000, async index => { await GetMember(index, createdId); });
            }

            await Task.Delay(TimeSpan.FromHours(1));
        }

        private static void RedisRepositoryErrorManager_ErrorOccuredEvent(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("SUBS ERROR! " + (e.ExceptionObject as Exception).Message);
        }

        private static void MemberRepository_SubscriptionTriggered(object sender, Member entity)
        {
            Console.WriteLine("Subscription triggered! Id: " + entity.Id);
        }

        private static async Task GetMember(int index, string id)
        {
            try
            {
                using (MemberRepository memberRepository = new())
                {
                    //var member = memberRepository.GetByIdAsync(id).GetAwaiter().GetResult();
                    var member = await memberRepository.GetByIdAsync(id);
                    //var member = memberRepository.GetById(id);

                    Console.WriteLine(index.ToString().PadLeft(5, '0') + ". " + member.Id);

                    memberRepository.Save(forceUpdate: true);

                    //await Task.Delay(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
