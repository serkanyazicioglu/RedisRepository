[![Build Status](https://dev.azure.com/serkanyazicioglu/serkanyazicioglu/_apis/build/status/serkanyazicioglu.RedisRepository?branchName=master)](https://dev.azure.com/serkanyazicioglu/serkanyazicioglu/_build/latest?definitionId=2&branchName=master)
[![NuGet](https://img.shields.io/nuget/v/Nhea.Data.Repository.RedisRepository.svg)](https://www.nuget.org/packages/Nhea.Data.Repository.RedisRepository/)

# Nhea Redis Repository

Redis base repository classes.


## Getting Started

Nhea Redis Repository is on NuGet. You may install Nhea Redis Repository via NuGet Package manager.

https://www.nuget.org/packages/Nhea.Data.Repository.RedisRepository/

```
Install-Package Nhea.Data.Repository.RedisRepository
```

### Prerequisites

Project is built with .NET Standard 2.1

This project references 
-	Nhea > 2.0.0.4
-	StackExchange.Redis > 2.1.58
-	Newtonsoft.Json > 12.0.3
-	System.Runtime.Caching > 4.7.0

### Configuration

First of all creating a base repository class is a good idea to set basic properties like connection string.

```
public abstract class BaseRedisRepository<T> : Nhea.Data.Repository.RedisRepository.BaseRedisRepository<T>
         where T : RedisDocument, new()
{
    public override string ConnectionString => "127.0.0.1:6379,abortConnect=false,connectTimeout=10000,connectRetry=10";
}
```
You may remove the abstract modifier if you want to use generic repositories or you may create individual repository classes for each of your objects if you need to set specific properties.
```
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
```
Then in your code just initalize a new instance of your class and call appropriate methods for your needs.

```
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
```

### Subscription

You can use both PubSub and Keyspaces feature on Redis.

#### PubSub
There are two options to use this feature. You may call Publish method on repository directly or set publish parameter to true on Save method. When subscribing you subscription type must be set to PubSub.
```
using (MemberRepository memberRepository = new MemberRepository())
{
    memberRepository.SubscriptionTriggered += MemberRepository_SubscriptionTriggered;

    //You can manually subscribe for custom events. When EnableCaching is set to true you don't have to call Subscribe method.
    memberRepository.Subscribe("*", SubscriptionTypes.PubSub);

    //Set publish to true
    memberRepository.Save(publish: true);

    //Or publish a custom message
    memberRepository.Publish("key", "value");
}
```

#### Keyspaces
In order to use this feature your Redis server must have keyspaces enabled. When keyspaces are enabled redis automatically pushes all CRUD events to your subscribers.

Run following command via redis-cli. 'KEA' is a variable and enables all events. Go to official page for further information about keyspace parameters: https://redis.io/topics/notifications.
```
config set notify-keyspace-events KEA
```
For listening events just initialize a new repository and bind an event to trigger.

```
using (MemberRepository memberRepository = new MemberRepository())
{
    memberRepository.SubscriptionTriggered += MemberRepository_SubscriptionTriggered;
}
```
Then all you have to do is just listening to this callback.
```
private static void MemberRepository_SubscriptionTriggered(object sender, Member entity)
{
    Console.WriteLine("Entity has changes! Id: " + entity.Id.ToString());
}
```
#### Caching
Nhea redis repostories has another in memory caching layer. To opt-in to this feature you can set EnableCaching property to true on your repository. This way your repository automatically listens to publishers and stores them on it's own cache.

If you want use keyspaces remember to enable keyspaces on your server. Otherwise you can use PubSub.

```
public class MemberRepository : BaseRedisRepository<Member>
{
    protected override bool EnableCaching => true;
}
```