﻿using AMP;
using AMP.DedicatedServer;
using AMP.DedicatedServer.Plugins;
using AMP.Events;
using AMP.Logging;
using AMP.Network.Data;
using AMP.Network.Packets.Implementation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using System.Threading;
using UnityEngine;

namespace MurderMystery
{
    public class MurderMystery : AMP_Plugin
    {
        public override string NAME => "MurderMystery";
        public override string AUTHOR => "LetsJustPlay";
        public override string VERSION => "0.3";

        private bool gameRunning = false;

        private MurderMysteryConfig config;

        private ClientData murderer;
        private ClientData detective;

        private List<ClientData> players = new List<ClientData>();
        private List<ClientData> citizens = new List<ClientData>();
        private List<ClientData> deadPlayers = new List<ClientData>();

        private CancellationTokenSource deadCancelTokenSource;
        private CancellationTokenSource matchCancelTokenSource;
        private CancellationToken deadCancelToken;
        private CancellationToken matchCancelToken;


        internal class MurderMysteryConfig : PluginConfig
        {
            public int requiredPlayerCount = 3;
            public float matchTime = 300.0f;
            public float intermissionTime = 10.0f;
        }


        private void deadTick(CancellationToken cancellationToken)
        {
            while (gameRunning && !cancellationToken.IsCancellationRequested)
            {
                List<ClientData> localClients = deadPlayers;
                int[] deadPlayerIds = localClients.Select(i => i.ClientId).ToArray();
                foreach (ClientData client in localClients)
                {
                    Vector3 pos = client.player.Position;
                    pos.y += 1000;
                    ModManager.serverInstance.netamiteServer.SendToAllExcept(
                        new PlayerPositionPacket(
                            playerId: client.ClientId,
                            handLeftPos: new Vector3(),
                            handLeftRot: new Vector3(),
                            handRightPos: new Vector3(),
                            handRightRot: new Vector3(),
                            headPos: new Vector3(),
                            headRot: new Vector3(),
                            playerPos: pos,
                            playerRot: 0
                        ),
                        deadPlayerIds
                    );
                }
                Thread.Sleep(100);
            }
        }

        private void matchLoop(CancellationToken cancellationToken)
        {
            float timer = config.matchTime;
            while (timer > 0 && !cancellationToken.IsCancellationRequested)
            {
                ModManager.serverInstance.netamiteServer.SendToAll(
                    new DisplayTextPacket("matchTimer", $"{timer}", Color.white, new Vector3(-1, 0, 2), true, true, 1)
                );
                Thread.Sleep(1000);
                timer--;
            }

            ModManager.serverInstance.netamiteServer.SendToAll(
                new DisplayTextPacket("endGameNotify", "Stalemate\nThe murderer failed to kill everyone and the detective failed to find the murderer.", Color.white, new Vector3(0,0,2), true, true, 5)
            );
            Thread.Sleep(5000);
            gameRunning = false;
        }

        private void intermissionLoop()
        {
            float timer = config.intermissionTime;
            while (timer > 0)
            {
                ModManager.serverInstance.netamiteServer.SendToAll(
                    new DisplayTextPacket("intermissionTimer", $"The match will start in {timer}.", Color.white, new Vector3(-1, 0, 2), true, true, 1)
                );
                Thread.Sleep(1000);
                timer--;
            }

            ModManager.serverInstance.netamiteServer.SendToAll(
                new DisplayTextPacket("matchStart", "The match is starting.", Color.white, new Vector3(-1, 0, 2), true, true, 1)
            );

            gameRunning = true;
            System.Random rand = new System.Random();

            murderer = players[rand.Next(players.Count())];
            murderer.SetDamageMultiplicator(10);
            Log.Info($"{murderer.ClientName} is the murderer");

            ClientData tryDetective = players[rand.Next(players.Count())];
            while (tryDetective.ClientId == murderer.ClientId)
            {
                tryDetective = players[rand.Next(players.Count())];
            }
            detective = tryDetective;
            detective.SetDamageMultiplicator(10);
            Log.Info($"{detective.ClientName} is the detective");

            citizens.Clear();
            foreach (ClientData client in players)
            {
                if (client.ClientId != murderer.ClientId && client.ClientId != detective.ClientId)
                {
                    citizens.Add(client);
                    client.SetDamageMultiplicator(0);
                    Log.Info($"{client.ClientName} is a citizen");
                }
            }
            deadPlayers.Clear();

            foreach (ClientData client in citizens)
            {
                ModManager.serverInstance.netamiteServer.SendTo(
                    client.ClientId,
                    new DisplayTextPacket("citizenNotify", "You are a Citizen.\nStay alive and help the detective.", Color.white, new Vector3(0,0,2), true, true, 2)
                );
            }
            ModManager.serverInstance.netamiteServer.SendTo(
                murderer.ClientId,
                new DisplayTextPacket("murdererNotify", "You are the Murderer.\nKill all the other players and avoid being caught by the detective.", Color.red, new Vector3(0,0,2), true, true, 2)
            );
            ModManager.serverInstance.netamiteServer.SendTo(
                detective.ClientId,
                new DisplayTextPacket("murdererNotify", "You are the Detective.\nProtect the Citizens and find the Murderer.", Color.blue, new Vector3(0, 0, 2), true, true, 2)
            );

            Thread.Sleep(1000);
            deadCancelTokenSource = new CancellationTokenSource();
            deadCancelToken = deadCancelTokenSource.Token;
            matchCancelTokenSource = new CancellationTokenSource();
            matchCancelToken = matchCancelTokenSource.Token;

            Thread match = new Thread(() => matchLoop(matchCancelToken));
            Thread deadLoop = new Thread(() => deadTick(deadCancelToken));
            match.Start();
            deadLoop.Start();
        }

        public override void OnStart()
        {
            ServerEvents.onPlayerJoin += OnPlayerJoin;
            ServerEvents.onPlayerQuit += OnPlayerQuit;
            ServerEvents.onPlayerKilled += OnPlayerKilled;

            config = (MurderMysteryConfig)GetConfig();
        }

        public void OnPlayerKilled(ClientData killed, ClientData killer)
        {
            if (killed.ClientId == murderer.ClientId)
            {
            }
        }

        public void OnPlayerJoin(ClientData client)
        {
            players.Add(client);
            if (gameRunning)
            {
                deadPlayers.Add(client);
                client.SetDamageMultiplicator(0);
                ModManager.serverInstance.netamiteServer.SendTo(
                    client,
                    new DisplayTextPacket("deadNotify", "You have joined during a match and have been automatically murdered.", Color.white, new Vector3(0,0,2), true, true, 5)
                );
            }
            else
            {
                ModManager.serverInstance.netamiteServer.SendToAll(
                    new DisplayTextPacket("playerJoinNotify", $"{client.ClientName} has joined the server.\n{players.Count}/{config.requiredPlayerCount}", Color.white, new Vector3(0,0,2), true, true, 1)
                );

                Thread.Sleep(1000);

                if (players.Count >= config.requiredPlayerCount)
                {
                    ModManager.serverInstance.netamiteServer.SendToAll(
                        new DisplayTextPacket("matchStart", $"Enough players have joined. Starting match.", Color.white, new Vector3(0, 0, 2), true, true, 1)
                    );

                    Thread intermission = new Thread(intermissionLoop);
                    intermission.Start();
                }
            }
        }

        public void OnPlayerQuit(ClientData client)
        {
            players.Remove(players.Where(i => i.ClientId == client.ClientId).First());

            try
            {
                citizens.Remove(citizens.Where(i => i.ClientId == client.ClientId).First());
            }
            catch
            {
                try
                {
                    deadPlayers.Remove(deadPlayers.Where(i => i.ClientId == client.ClientId).First());
                }
                catch { }
            }

        }
    }
}
