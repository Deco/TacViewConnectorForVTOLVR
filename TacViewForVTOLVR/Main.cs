using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Harmony;
using JetBrains.Annotations;
// using Priority_Queue;
using UnityEngine;
using UnityEngine.Assertions;
using VTNetworking;
using VTOLVR.Multiplayer;

namespace TacViewForVTOLVR
{
    public class Main : VTOLMOD
    {
        public static Main instance;

        public Session session;

        void SeriouslyWhatTheFuck()
        {
            VTOLMPLobbyManager.currentLobby.SetData("lMods", "");
            VTOLMPLobbyManager.currentLobby.SetData("lModCount", "0");
        }

        public override void ModLoaded()
        {
            Assert.IsTrue(instance == null);
            instance = this;

            Log("HEY!!!!");

            HarmonyInstance.DEBUG = true;
            var harmony = HarmonyInstance.Create("deco.tacviewconnectorforvtolvr.patch");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log("Patched!!!!");

            SeriouslyWhatTheFuck();
        }

        //This method is called every frame by Unity. Here you'll probably put most of your code
        void Update()
        {
        }

        //This method is like update but it's framerate independent. This means it gets called at a set time interval instead of every frame. This is useful for physics calculations
        void FixedUpdate()
        {
            session?.OnFixedUpdate();
        }

        public void StartLogging()
        {
            SeriouslyWhatTheFuck();

            if (session != null) StopLogging();

            var folderPath = $"TacView\\{DateTime.UtcNow.ToString("yyyy-MM-dd HH꞉mm")}";
            Directory.CreateDirectory(folderPath);
            session = new Session(folderPath);

            FlightSceneManager.instance.OnExitScene += StopLogging;
        }

        public void StopLogging()
        {
            if (session == null) return;
            session.Stop();
            session = null;
        }
    }

    enum TrackeeFlavour
    {
        Player,
        Bullet,
        Chaff,
        Flare,
        Jetsam,
        Missile,
        Rocket,
        Bomb,

        AIAircraft,
        AISeaUnit,
        AIGroundUnit,

        Airport,
    }

    enum IDPrefix : byte
    {
        BulletChaffFlare = 0x1,
        BombMissileRocket = 0x2,
        Aircraft = 0xa,
        Other = 0xf,
    }

    class Trackee : PriorityQueueNode<Trackee>
    {
        public TrackeeFlavour flavour;
        public ulong id;

        // public float lastFastRefreshTime = 0;
        // public float lastSlowRefreshTime = 0;
        public float nextRefreshTime;

        [CanBeNull] public Actor actor;
        [CanBeNull] public Rocket rocket;
        [CanBeNull] public Bullet bullet;
        public ChaffCountermeasure.Chaff? chaff;
        [CanBeNull] public CMFlare flare;
        [CanBeNull] public HPEquippable equippable;

        public bool refreshABitFaster = false;

        public ulong aircraft_lastLockedIdOrZero = 0;

        public Trackee(TrackeeFlavour flav, ulong subId)
        {
            flavour = flav;

            string prefix = "f"; // Other
            switch (flav)
            {
                case TrackeeFlavour.Bullet:
                case TrackeeFlavour.Chaff:
                case TrackeeFlavour.Flare:
                    prefix = "1";
                    break;

                case TrackeeFlavour.Bomb:
                case TrackeeFlavour.Missile:
                case TrackeeFlavour.Rocket:
                    prefix = "2";
                    break;

                case TrackeeFlavour.Player:
                case TrackeeFlavour.AIAircraft:
                    prefix = "a";
                    break;
            }

            id = Convert.ToUInt64($"{prefix}{subId:x}", 16);
        }

        public override int CompareTo(Trackee other) => -nextRefreshTime.CompareTo(other.nextRefreshTime);
    }

    public class Session
    {
        string folderPath;
        string acmiFilePath;
        StreamWriter acmiWriter;
        private PriorityQueue<Trackee> trackeeQueue = new PriorityQueue<Trackee>(8);
        private float startGameTime;
        private bool doneAirports = false;

        ulong nextId_BulletChaffFlare = 0;
        ulong nextId_BombMissileRocket = 0;
        ulong nextId_Aircraft = 0;
        ulong nextId_Other = 0;

        private Dictionary<Actor, Trackee> actorTrackeeMap = new Dictionary<Actor, Trackee>();

        bool Track(Trackee trackee, float? overrideNextRefreshInterval = null)
        {
            if (trackeeQueue.Count == trackeeQueue.MaxSize)
                trackeeQueue.Resize(trackeeQueue.MaxSize * 2);

            if (trackee.actor != null)
                Main.instance.Log($"Tracking: {trackee.id:x} {trackee.actor}");
            else if (trackee.bullet != null)
                Main.instance.Log($"Tracking: {trackee.id:x} {trackee.bullet}");
            else
                Main.instance.Log($"Tracking: {trackee.id:x} {trackee.flavour}");

            if (trackee.actor != null)
            {
                if (actorTrackeeMap.ContainsKey(trackee.actor))
                    return false;
                actorTrackeeMap[trackee.actor] = trackee;
            }

            trackee.nextRefreshTime = Time.time + (overrideNextRefreshInterval ?? 1f / 16f);
            trackeeQueue.Enqueue(trackee);

            return true;
        }

        public Session(string _folderPath)
        {
            folderPath = _folderPath;

            acmiFilePath = $"{folderPath}\\log.acmi";
            acmiWriter   = new StreamWriter(acmiFilePath, false, new UTF8Encoding(), 1024 * 1024);

            startGameTime = Time.time;

            acmiWriter.WriteLine("FileType=text/acmi/tacview");
            acmiWriter.WriteLine("FileVersion=2.1");
            acmiWriter.WriteLine("0,DataSource=VTOL VR");
            acmiWriter.WriteLine("0,DataRecorder=TacView Connector for VTOL VR");
            acmiWriter.WriteLine($"0,ReferenceTime={DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")}");
            // acmiWriter.WriteLine($"0,Author=");
            // acmiWriter.WriteLine($"0,Title=");
            // acmiWriter.WriteLine($"0,Category=");
            acmiWriter.WriteLine($"0,Briefing=Eat Banana");
            acmiWriter.WriteLine($"0,Debriefing=Banana Eaten");
            // acmiWriter.WriteLine($"0,Comments=");
        }

        ~Session()
        {
            Stop();
        }

        public void OnMultiplayerSpawn(GameObject vehicleObj)
        {
            Actor playerActor = vehicleObj.GetComponent<Actor>();
            OnPlayerSpawn(playerActor);
        }

        public void OnPlayerSpawn(Actor actor)
        {
            if (!doneAirports) DoAirports();

            var trackee = new Trackee(TrackeeFlavour.Player, nextId_Aircraft++);
            trackee.actor = actor;
            if (!Track(trackee)) return;

            string name = actor.name.Replace("(Clone)", "");
            string type = "FixedWing";
            if (name.Contains("AH-94"))
            {
                type              = "Rotorcraft";
                trackee.refreshABitFaster = true;
            }

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(actor.transform.position);
            var fi = actor.flightInfo;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type={type}" +
                                 $",CallSign={actor.designation}" +
                                 $",Name={name}" +
                                 $",Color={(actor.team == Teams.Allied ? "Blue" : "Red")}" +
                                 $",Pilot={actor.actorName}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{fi.roll:F3}|{fi.pitch:F3}|{fi.heading:F3}" +
                                 $",IAS={AerodynamicsController.fetch.IndicatedAirspeed(fi.airspeed, fi.rb.position):F2}" +
                                 $",TAS={fi.airspeed:F2}" +
                                 $",AOA={fi.aoa:F2}" +
                                 $",AGL={fi.radarAltitude:F2}");
        }

        public void OnAIAircraftSpawn(Actor actor)
        {
            if (!doneAirports) DoAirports();

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(actor.transform.position);
            var fi = actor.flightInfo;

            var trackee = new Trackee(TrackeeFlavour.AIAircraft, nextId_Aircraft++);
            trackee.actor = actor;
            Track(trackee);

            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type=FixedWing" +
                                 $",CallSign={actor.designation}" +
                                 $",Name={actor.name.Replace("(Clone)", "")}" +
                                 $",Color={(actor.team == Teams.Allied ? "Blue" : "Red")}" +
                                 $",Pilot={actor.actorName}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{fi.roll:F3}|{fi.pitch:F3}|{fi.heading:F3}" +
                                 $",IAS={AerodynamicsController.fetch.IndicatedAirspeed(fi.airspeed, fi.rb.position):F2}" +
                                 $",TAS={fi.airspeed:F2}" +
                                 $",AOA={fi.aoa:F2}" +
                                 $",AGL={fi.radarAltitude:F2}");
        }

        public void OnMissileFire(Actor actor)
        {
            var trackee = new Trackee(TrackeeFlavour.Missile, (nextId_BombMissileRocket = (nextId_BombMissileRocket + 1) % 0xFFFF));
            trackee.actor = actor;
            Track(trackee);

            string type = "Missile";
            var missile = trackee.actor.GetMissile();
            if (missile != null && missile.guidanceMode == Missile.GuidanceModes.Bomb || missile.guidanceMode == Missile.GuidanceModes.GPS)
                type = "Bomb";

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(actor.transform.position);
            var ang = actor.transform.eulerAngles;
            var owner = actor.GetMissile().launchedByActor;
            var ownerIdOrZero = (owner != null && actorTrackeeMap.ContainsKey(owner)) ? actorTrackeeMap[owner].id : 0;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type={type}" +
                                 $",Name={actor.actorName}" +
                                 $",Color={(actor.team == Teams.Allied ? "Blue" : "Red")}" +
                                 $"{(ownerIdOrZero != 0 ? $",Parent={ownerIdOrZero:x}" : "")}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}");
        }

        public void OnRocketFire(Rocket rocket)
        {
            var trackee = new Trackee(TrackeeFlavour.Rocket, (nextId_BombMissileRocket = (nextId_BombMissileRocket + 1) % 0xFFFF));
            trackee.rocket = rocket;
            Track(trackee);

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(rocket.transform.position);
            var ang = rocket.transform.eulerAngles;
            var owner = Traverse.Create(rocket).Field("sourceActor").GetValue<Actor>();
            var ownerIdOrZero = (owner != null && actorTrackeeMap.ContainsKey(owner)) ? actorTrackeeMap[owner].id : 0;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type=Rocket" +
                                 $"{(ownerIdOrZero != 0 ? $",Parent={ownerIdOrZero:x}" : "")}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}");
        }

        public void OnBulletFire(Bullet bullet)
        {
            var trackee = new Trackee(TrackeeFlavour.Bullet, (nextId_BulletChaffFlare = (nextId_BulletChaffFlare + 1) % 0xFFFF));
            trackee.bullet = bullet;
            Track(trackee);

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(bullet.transform.position);
            var owner = Traverse.Create(bullet).Field("sourceActor").GetValue<Actor>();
            var ownerIdOrZero = (owner != null && actorTrackeeMap.ContainsKey(owner)) ? actorTrackeeMap[owner].id : 0;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type=Bullet" +
                                 $"{(owner != null ? $",Color={(owner.team == Teams.Allied ? "Blue" : "Red")}" : "")}" +
                                 $"{(ownerIdOrZero != 0 ? $",Parent={ownerIdOrZero:x}" : "")}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");
        }

        public void OnChaffFire(ChaffCountermeasure cm)
        {
            var chaffs = Traverse.Create(cm).Field("chaffs").GetValue<ChaffCountermeasure.Chaff[]>();
            var nextChaffId = Traverse.Create(cm).Field("nextChaffId").GetValue<int>();
            var prevChaffId = nextChaffId - 1;
            if (prevChaffId < 0) prevChaffId += chaffs.Length;
            var latestChaff = chaffs[prevChaffId];

            var trackee = new Trackee(TrackeeFlavour.Chaff, (nextId_BulletChaffFlare = (nextId_BulletChaffFlare + 1) % 0xFFFF));
            trackee.chaff = latestChaff;
            Track(trackee, Traverse.Create(latestChaff).Field("decayTime").GetValue<float>());

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(latestChaff.position);
            var owner = cm.myActor;
            var ownerIdOrZero = (owner != null && actorTrackeeMap.ContainsKey(owner)) ? actorTrackeeMap[owner].id : 0;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type=Chaff" +
                                 $"{(ownerIdOrZero != 0 ? $",Parent={ownerIdOrZero:x}" : "")}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");
        }

        public void OnChaffFire(ChaffCountermeasure.Chaff chaff, ChaffCountermeasure cm)
        {
            var trackee = new Trackee(TrackeeFlavour.Chaff, (nextId_BulletChaffFlare = (nextId_BulletChaffFlare + 1) % 0xFFFF));
            trackee.chaff = chaff;
            Track(trackee, Traverse.Create(chaff).Field("decayTime").GetValue<float>());

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(chaff.position);
            var owner = cm.myActor;
            var ownerIdOrZero = (owner != null && actorTrackeeMap.ContainsKey(owner)) ? actorTrackeeMap[owner].id : 0;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type=Chaff" +
                                 $"{(ownerIdOrZero != 0 ? $",Parent={ownerIdOrZero:x}" : "")}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");
        }

        public void OnFlareFire(CMFlare flare)
        {
            var trackee = new Trackee(TrackeeFlavour.Flare, (nextId_BulletChaffFlare = (nextId_BulletChaffFlare + 1) % 0xFFFF));
            trackee.flare = flare;

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(flare.transform.position);
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type=Flare" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");
        }

        public void OnEquippableJettison(HPEquippable equippable)
        {
            var trackee = new Trackee(TrackeeFlavour.Jetsam, nextId_Other++);
            trackee.equippable = equippable;

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(equippable.transform.position);
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type=Misc+Minor" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");
        }

        public void OnGroundUnitSpawn(Actor actor)
        {
            var trackee = new Trackee(TrackeeFlavour.AIGroundUnit, nextId_Other++);
            trackee.actor = actor;
            Track(trackee);

            string type = "Ground";
            if (actor.unitSpawn is AIFixedSAMSpawn)
                type = "Ground+AntiAircraft";
            else if (actor.unitSpawn is AILockingRadarSpawn)
                type = "Ground"; // todo: ground radar type
            else if (actor.unitSpawn is ArtilleryUnitSpawn)
                type = "Ground"; // todo: ground artillery type

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(actor.transform.position);
            var ang = trackee.actor.transform.eulerAngles;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type={type}" +
                                 $",Name={actor.name.Replace("(Clone)", "")}" +
                                 $",Color={(actor.team == Teams.Allied ? "Blue" : "Red")}" +
                                 $",Pilot={actor.actorName}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}");
        }

        public void OnSeaUnitSpawn(Actor actor)
        {
            var trackee = new Trackee(TrackeeFlavour.AISeaUnit, nextId_Other++);
            trackee.actor = actor;
            Track(trackee);

            string type = "Ground";
            if (actor.unitSpawn is AIFixedSAMSpawn)
                type = "Ground+AntiAircraft";
            else if (actor.unitSpawn is AILockingRadarSpawn)
                type = "Ground"; // todo: ground radar type
            else if (actor.unitSpawn is ArtilleryUnitSpawn)
                type = "Ground"; // todo: ground artillery type

            var gps = VTMapManager.fetch.WorldPositionToGPSCoords(actor.transform.position);
            var ang = trackee.actor.transform.eulerAngles;
            acmiWriter.WriteLine($"{trackee.id:x}" +
                                 $",Type={type}" +
                                 $",Name={actor.name.Replace("(Clone)", "")}" +
                                 $",Color={(actor.team == Teams.Allied ? "Blue" : "Red")}" +
                                 $",Pilot={actor.actorName}" +
                                 $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}");
        }

        private void DoAirports()
        {
            foreach (var am in VTMapManager.fetch.airports)
            {
                var trackee = new Trackee(TrackeeFlavour.Airport, nextId_Other++);
                Track(trackee);

                var gps = VTMapManager.fetch.WorldPositionToGPSCoords(am.transform.position);
                var ang = am.transform.eulerAngles;
                acmiWriter.WriteLine($"{trackee.id:x}" +
                                     $",Type=Airport" +
                                     $",Name={am.airportName}" +
                                     $",Color={(am.team == Teams.Allied ? "Blue" : "Red")}" +
                                     $"Width=200,Length=80,Height=2" + // todo: airport size
                                     $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}");
            }
        }

        public void Stop()
        {
            if (acmiWriter == null) return;
            acmiWriter.WriteLine($"0,RecordingTime={DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")}");
            acmiWriter.Close();
            acmiWriter = null;
        }

        public void OnFixedUpdate()
        {
            bool doneTimeRef = false;
            while (trackeeQueue.Count > 0 && Time.time >= trackeeQueue.First.nextRefreshTime)
            {
                var trackee = trackeeQueue.Dequeue();
                float interval;
                Action<string> write = str =>
                {
                    if (!doneTimeRef)
                    {
                        doneTimeRef = true;
                        acmiWriter.WriteLine($"#{Time.time - startGameTime}");
                    }

                    acmiWriter.WriteLine(str);
                };

                if (trackee.flavour == TrackeeFlavour.Player || trackee.flavour == TrackeeFlavour.AIAircraft)
                {
                    if (trackee.actor == null || !trackee.actor.alive) goto dead;
                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.actor.transform.position);
                    var fi = trackee.actor.flightInfo;

                    var lockingRadars = trackee.actor.GetLockingRadars();
                    ulong anyLockedIdOrZero = 0;
                    foreach (var lockingRadar in lockingRadars)
                    {
                        if (lockingRadar.currentLock != null
                            && lockingRadar.currentLock.actor != null
                            && actorTrackeeMap.ContainsKey(lockingRadar.currentLock.actor))
                        {
                            anyLockedIdOrZero = actorTrackeeMap[lockingRadar.currentLock.actor].id;
                            break;
                        }
                    }

                    string lockedOut = "";
                    if (anyLockedIdOrZero != trackee.aircraft_lastLockedIdOrZero)
                    {
                        lockedOut = $",LockedTargetMode={(anyLockedIdOrZero == 0 ? "0" : $"1,LockedTarget={anyLockedIdOrZero:x}")}";
                        trackee.aircraft_lastLockedIdOrZero = anyLockedIdOrZero;
                    }

                    write($"{trackee.id:x}" +
                          $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{fi.roll:F3}|{fi.pitch:F3}|{fi.heading:F3}" +
                          $",IAS={AerodynamicsController.fetch.IndicatedAirspeed(fi.airspeed, fi.rb.position):F2}" +
                          $",TAS={fi.airspeed:F2}" +
                          $",AOA={fi.aoa:F2}" +
                          $",AGL={fi.radarAltitude:F2}" +
                          lockedOut +

                          // $",Fuel={}" +
                          // $",Afterburner={}" +
                          // $",Throttle={}" +

                          // $",RadarMode={}" +

                          // $",LandingGear={}" +
                          // $",Tailhook={}" +

                          // $",Mach={}" +
                          $"");

                    var ang = trackee.actor.transform.eulerAngles;
                    write($"// {fi.roll:F3}, {fi.pitch:F3}, {fi.heading:F3} vs {-ang.z:F3}, {-ang.x:F3}, {ang.y:F3}");

                    interval = 1f / 10f;
                }
                else if (trackee.flavour == TrackeeFlavour.Missile || trackee.flavour == TrackeeFlavour.Bomb)
                {
                    if (trackee.actor == null || !trackee.actor.alive) goto dead;
                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.actor.transform.position);
                    var ang = trackee.actor.transform.eulerAngles;

                    ulong lockedIdOrZero = 0;
                    var missile = trackee.actor.GetMissile();
                    if (missile != null)
                    {
                        Actor lockedActor = null;
                        if (missile.guidanceMode == Missile.GuidanceModes.Radar)
                            lockedActor = missile.lockingRadar.currentLock?.actor;
                        else if (missile.guidanceMode == Missile.GuidanceModes.Heat)
                            lockedActor = missile.heatSeeker.likelyTargetActor;
                        else if (missile.guidanceMode == Missile.GuidanceModes.Optical)
                            lockedActor = missile.opticalTargetActor;
                        else if (missile.guidanceMode == Missile.GuidanceModes.AntiRad)
                            lockedActor = missile.antiRadTargetActor;

                        if (lockedActor != null && actorTrackeeMap.ContainsKey(lockedActor))
                            lockedIdOrZero = actorTrackeeMap[lockedActor].id;
                    }

                    write($"{trackee.id:x}" +
                          $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}" +
                          $",LockedTargetMode={(lockedIdOrZero == 0 ? "0" : $"1,LockedTarget={lockedIdOrZero:x}")}");

                    interval = 1f / (trackee.flavour == TrackeeFlavour.Bomb ? 2f : 8f);
                }
                else if (trackee.flavour == TrackeeFlavour.Rocket)
                {
                    if (trackee.rocket == null || !trackee.rocket.isActiveAndEnabled) goto dead;

                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.rocket.transform.position);
                    write($"{trackee.id:x}" +
                          $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");

                    interval = 1f / 4f;
                }
                else if (trackee.flavour == TrackeeFlavour.Bullet)
                {
                    if (trackee.bullet == null || !trackee.bullet.isActiveAndEnabled) goto dead;

                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.bullet.transform.position);
                    write($"{trackee.id:x}" +
                          $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");

                    interval = 1f / 2f;
                }
                else if (trackee.flavour == TrackeeFlavour.Chaff)
                {
                    goto dead;
                }
                else if (trackee.flavour == TrackeeFlavour.Flare)
                {
                    if (trackee.flare == null || !trackee.flare.isActiveAndEnabled) goto dead;

                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.flare.transform.position);
                    write($"{trackee.id:x}" +
                          $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");

                    interval = 1f / 3f;
                }
                else if (trackee.flavour == TrackeeFlavour.Jetsam)
                {
                    if (trackee.equippable == null || !trackee.equippable.isActiveAndEnabled) goto dead;

                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.equippable.transform.position);
                    write($"{trackee.id:x}" +
                          $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}");

                    interval = 1f;
                }
                else if (trackee.flavour == TrackeeFlavour.AISeaUnit || trackee.flavour == TrackeeFlavour.AIGroundUnit)
                {
                    // todo: AISeaUnit and AIGroundUnit
                    interval = 1f;
                }
                else if (trackee.flavour == TrackeeFlavour.AIGroundUnit)
                {
                    if (trackee.actor == null || !trackee.actor.alive) goto dead;

                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.actor.transform.position);
                    var ang = trackee.actor.transform.eulerAngles;
                    acmiWriter.WriteLine($"{trackee.id:x}" +
                                         $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}");
                    interval = 4f;
                }
                else if (trackee.flavour == TrackeeFlavour.AISeaUnit)
                {
                    if (trackee.actor == null || !trackee.actor.alive) goto dead;

                    var gps = VTMapManager.fetch.WorldPositionToGPSCoords(trackee.actor.transform.position);
                    var ang = trackee.actor.transform.eulerAngles;
                    acmiWriter.WriteLine($"{trackee.id:x}" +
                                         $",T={gps.y:F7}|{gps.x:F7}|{gps.z:F7}|{-ang.z:F3}|{-ang.x:F3}|{ang.y:F3}");
                    interval = 3f;
                }
                else if (trackee.flavour == TrackeeFlavour.Airport)
                {
                    // todo: ATC calls?
                    interval = 30f;
                }
                else throw new WhatTheFuckException();

                if (trackee.refreshABitFaster) interval /= 2.0f;

                trackee.nextRefreshTime = Time.time + interval;
                trackeeQueue.Enqueue(trackee);

                break;
                dead:
                {
                    write($"-{trackee.id:x}");
                    if (trackee.actor != null)
                    {
                        Main.instance.Log($"Trackee dead: {trackee.id:x} {trackee.actor}");
                        actorTrackeeMap.Remove(trackee.actor);
                    }
                    else if (trackee.bullet != null)
                    {
                        Main.instance.Log($"Trackee dead: {trackee.id:x} {trackee.bullet}");
                    }
                    else
                    {
                        Main.instance.Log($"Trackee dead: {trackee.id:x} {trackee.flavour}");
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CheckNotNull(string what, object thing,
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string caller = null)
        {
            if (thing == null)
                throw new Exception($"L{lineNumber} {caller} {what} = {thing}");
        }
    }

    [HarmonyPatch(typeof(EndMission), nameof(EndMission.Initialize))]
    class Patch_EndMission_Initialize
    {
        static bool Prefix(EndMission __instance)
        {
            Main.instance.StartLogging();
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerSpawn), nameof(PlayerSpawn.OnSpawnUnit))]
    class Patch_PlayerSpawn_OnSpawnUnit
    {
        static void Postfix(PlayerSpawn __instance)
        {
            Main.instance.session?.OnPlayerSpawn(__instance.actor);
        }
    }

    [HarmonyPatch(typeof(MultiplayerSpawn), nameof(MultiplayerSpawn.SetupSpawnedVehicle))]
    class Patch_MultiplayerSpawn_SetupSpawnedVehicle
    {
        static void Postfix(MultiplayerSpawn __instance, GameObject vehicleObj)
        {
            Main.instance.session?.OnMultiplayerSpawn(vehicleObj);
        }
    }

    [HarmonyPatch(typeof(VTOLMPSceneManager), nameof(VTOLMPSceneManager.RPC_SetPlayerVehicleEntity))]
    class Patch_VTOLMPSceneManager_RPC_SetPlayerVehicleEntity
    {
        static void Postfix(VTOLMPSceneManager __instance, int entityID)
        {
            if (!(bool)(UnityEngine.Object)VTNetworkManager.instance.GetEntity(entityID))
                return;
            VTNetEntity entity = VTNetworkManager.instance.GetEntity(entityID);
            Main.instance.session?.OnMultiplayerSpawn(entity.gameObject);
        }
    }

    [HarmonyPatch(typeof(AIAircraftSpawn), nameof(AIAircraftSpawn.OnSpawnUnit))]
    class Patch_AIAircraftSpawn_OnSpawnUnit
    {
        static void Postfix(AIAircraftSpawn __instance)
        {
            Main.instance.session?.OnAIAircraftSpawn(__instance.actor);
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Fire))]
    class Patch_Missile_Fire
    {
        static void Postfix(Missile __instance)
        {
            Main.instance.session?.OnMissileFire(__instance.actor);
        }
    }

    [HarmonyPatch(typeof(Rocket), nameof(Rocket.Fire))]
    class Patch_Rocket_Fire
    {
        static void Postfix(Rocket __instance)
        {
            Main.instance.session?.OnRocketFire(__instance);
        }
    }

    [HarmonyPatch(typeof(Bullet), nameof(Bullet.Fire))]
    class Patch_Bullet_Fire
    {
        static void Postfix(Bullet __instance)
        {
            Main.instance.session?.OnBulletFire(__instance);
        }
    }

    // [HarmonyPatch(typeof(ChaffCountermeasure.Chaff), MethodType.Constructor, new[] { typeof(ChaffCountermeasure) })]
    // class ChaffCountermeasure_Chaff_Constructor1
    // {
    //     static void Postfix(ChaffCountermeasure.Chaff __instance, ChaffCountermeasure cm)
    //     {
    //         Main.instance.session?.OnChaffFire(__instance, cm);
    //     }
    // }

    [HarmonyPatch(typeof(ChaffCountermeasure), nameof(ChaffCountermeasure.AdvRdrChaff))]
    class ChaffCountermeasure_AdvRdrChaff
    {
        static void Postfix(ChaffCountermeasure __instance)
        {
            Main.instance.session?.OnChaffFire(__instance);
        }
    }

    [HarmonyPatch(typeof(CMFlare), nameof(CMFlare.OnEnable))]
    class Patch_CMFlare_OnEnable
    {
        static void Postfix(CMFlare __instance)
        {
            Main.instance.session?.OnFlareFire(__instance);
        }
    }

    [HarmonyPatch(typeof(HPEquippable), nameof(HPEquippable.Jettison))]
    class Patch_HPEquippable_Jettison
    {
        static void Postfix(HPEquippable __instance)
        {
            Main.instance.session?.OnEquippableJettison(__instance);
        }
    }

    [HarmonyPatch(typeof(GroundUnitSpawn), nameof(GroundUnitSpawn.OnSpawnUnit))]
    class GroundUnitSpawn_OnSpawnUnit
    {
        static void Postfix(GroundUnitSpawn __instance)
        {
            Main.instance.session?.OnGroundUnitSpawn(__instance.actor);
        }
    }

    [HarmonyPatch(typeof(AISeaUnitSpawn), nameof(AISeaUnitSpawn.OnSpawnUnit))]
    class AISeaUnitSpawn_OnSpawnUnit
    {
        static void Postfix(AISeaUnitSpawn __instance)
        {
            Main.instance.session?.OnSeaUnitSpawn(__instance.actor);
        }
    }

    internal class WhatTheFuckException : Exception
    {
    }

    //Patches the create lobby function to add the host's loaded mods to the lobby info
    [HarmonyPatch(typeof(VTMPMainMenu), nameof(VTMPMainMenu.LaunchMPGameForScenario))]
    class VTMPMainMenu_LaunchMPGameForScenario
    {
        static void Postfix()
        {
            if (VTOLMPLobbyManager.isLobbyHost)
            {
                Debug.Log("Setting mod data: ");
                VTOLMPLobbyManager.currentLobby.SetData("lMods", "");
                VTOLMPLobbyManager.currentLobby.SetData("lModCount", "0");
            }
        }
    }
}