using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;

namespace AkkaTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = @"
                akka {
                    stdout-loglevel = WARNING
                    loglevel = WARNING
                    log-config-on-start = off

                    actor {
                        provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                    }

                    remote {
                        log-remote-lifecycle-events = ERROR
                        helios.tcp {
                            hostname = ""127.0.0.1""
                            port = " + args[0] + @"
                        }
                    }

                    cluster {
                        seed-nodes = [" + string.Join(",", args.Skip(1).Select(x => $"\"akka.tcp://akkatest@127.0.0.1:{x}\""))  + @"],
                        auto-down-unreachable-after = off
                    }
                }";

            var actorSystem = ActorSystem.Create("akkatest", ConfigurationFactory.ParseString(config));
            var cluster = Cluster.Get(actorSystem);
            var pingActor = actorSystem.ActorOf<PingActor>("ping");
            

            while (true)
            {
                var command = Console.ReadLine();
                var commandArgs = command.Split(' ');
                switch (commandArgs[0].ToLowerInvariant())
                {
                    case "join":
                        Join(commandArgs, cluster);
                        break;
                    case "join-seed":
                        JoinSeedNodes(commandArgs, cluster);
                        break;
                    case "leave":
                        Leave(commandArgs, cluster);
                        break;
                    case "down":
                        Down(commandArgs, cluster);
                        break;
                    case "status":
                        Status(cluster);
                        break;
                    case "ping":
                        Ping(actorSystem, commandArgs);
                        break;
                    default:
                        Console.WriteLine($"Don't know command \"{command}\"");
                        break;

                }
            }
        }

        private static void Join(string[] commandArgs, Cluster cluster)
        {
            var address1 = Address.Parse($"akka.tcp://akkatest@127.0.0.1:{commandArgs[1]}");
            Console.WriteLine($"Joining {address1}");
            cluster.Join(address1);
        }

        private static void JoinSeedNodes(string[] commandArgs, Cluster cluster)
        {
            var builder = ImmutableList<Address>.Empty.ToBuilder();
            foreach (var port in commandArgs.Skip(1))
            {
                builder.Add(Address.Parse($"akka.tcp://akkatest@127.0.0.1:{port}"));
            }
            var list = builder.ToImmutable();
            Console.WriteLine("Joining Seed Nodes:\n" + string.Join("\n   ", list.Select(x => $"{x}")));
            cluster.JoinSeedNodes(list);
        }

        private static void Leave(string[] commandArgs, Cluster cluster)
        {
            var address3 = Address.Parse($"akka.tcp://akkatest@127.0.0.1:{commandArgs[1]}");
            Console.WriteLine($"Leaving {address3}");
            cluster.Leave(address3);
        }

        private static void Down(string[] commandArgs, Cluster cluster)
        {
            var address2 = Address.Parse($"akka.tcp://akkatest@127.0.0.1:{commandArgs[1]}");
            Console.WriteLine($"Downing {address2}");
            cluster.Down(address2);
        }

        private static void Status(Cluster cluster)
        {
            Console.WriteLine("Self: " + cluster.SelfUniqueAddress);
            Console.WriteLine("ClusterStatus: " + GetStatus(cluster));
            Console.WriteLine("ClusterLeader: " + cluster.ReadView.Leader);
            Console.WriteLine("Members:");
            Console.WriteLine("   " +
                              string.Join("\n   ", cluster.ReadView.Members.Select(x => $"{x.UniqueAddress} - {x.Status}")));
            Console.WriteLine("UnreachableMembers: ");
            Console.WriteLine("   " +
                              string.Join("\n   ",
                                  cluster.ReadView.Reachability.AllUnreachable.Select(x => $"{x.Address} {x.Uid}")));
        }

        private static void Ping(ActorSystem actorSystem, string[] commandArgs)
        {
            actorSystem.ActorSelection($"akka.tcp://akkatest@127.0.0.1:{commandArgs[1]}/user/ping")
                .Ask<string>("ping")
                .ContinueWith(x =>
                {
                    if (x.IsFaulted)
                    {
                        Console.WriteLine("Got faulty ping response: " + x.Exception?.ToString());
                    }
                    else
                    {
                        Console.WriteLine("Got ping response: " + x.Result);
                    }
                });
        }

        private static string GetStatus(Cluster cluster)
        {
            try
            {
                return cluster.ReadView.Status.ToString();
            }
            catch (Exception)
            {
                return "Got Exception";
                throw;
            }
            
        }
    }

    public class PingActor : ReceiveActor
    {
        public PingActor()
        {
            var cluster = Cluster.Get(Context.System);

            Receive<string>(x => x == "ping", s =>
            {
                Sender.Tell("pong from " + cluster.SelfUniqueAddress.ToString());
            });
        }
    }
}
