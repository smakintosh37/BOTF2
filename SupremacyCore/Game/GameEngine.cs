// File:GameEngine.cs
// Copyright (c) 2007 Mike Strobel
//
// This source code is subject to the terms of the Microsoft Reciprocal License (Ms-RL).
// For details, see <http://www.opensource.org/licenses/ms-rl.html>.
//
// All other rights reserved.

using Supremacy.AI;
using Supremacy.Annotations;
using Supremacy.Collections;
using Supremacy.Combat;
using Supremacy.Data;
using Supremacy.Diplomacy;
using Supremacy.Diplomacy.Visitors;
using Supremacy.Economy;
using Supremacy.Entities;
using Supremacy.Intelligence;
using Supremacy.Orbitals;
using Supremacy.Tech;
using Supremacy.Types;
using Supremacy.Universe;
using Supremacy.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

// DoTurn at Line 163

namespace Supremacy.Game
{
    /// <summary>
    /// The turn processing engine used in the game.
    /// </summary>
    public class GameEngine
    {

        public int _gameTurnNumber = 0;  // internal use
        public string blank = " ";
        readonly List<CivValue> CivValueList = new List<CivValue>();
        public string newline = Environment.NewLine;
        public int AAASpecialWidth1;
        public int AAASpecialHeight1;

        #region Public Members

        /// <summary>
        /// Occurs when the current turn phase has changed.
        /// </summary>
        public event TurnPhaseEventHandler TurnPhaseChanged;

        /// <summary>
        /// Occurs when the current turn phase has finished.
        /// </summary>
        public event TurnPhaseEventHandler TurnPhaseFinished;

        /// <summary>
        /// Occurs when combat is starting.
        /// </summary>
        public event CombatEventHandler CombatOccurring;

        /// <summary>
        /// Occurs when an invasion is starting.
        /// </summary>
        public event InvasionEventHandler InvasionOccurring;

        /// <summary>
        /// Occurs when a Fleet moves to a new location.
        /// </summary>
        public event EventHandler<ParameterEventArgs<Fleet>> FleetLocationChanged;

        public object GameContent { get; private set; }
        public object AppContext { get; private set; }

        #endregion

        #region Private Members
        /// <summary>
        /// Blocks the execution of the turn processing engine while waiting on players
        /// to submit combat orders.
        /// </summary>
        private readonly ManualResetEvent CombatReset = new ManualResetEvent(false);
        #endregion

        #region OnTurnPhaseChanged() Method
        /// <summary>
        /// Raises the <see cref="TurnPhaseChanged"/> event.
        /// </summary>
        /// <param name="game">The current game.</param>
        /// <param name="phase">The current turn phase.</param>
        private void OnTurnPhaseChanged(GameContext game, TurnPhase phase)
        {
            if (phase != TurnPhase.SendUpdates)
            {
                foreach (Scripting.ScriptedEvent scriptedEvent in game.ScriptedEvents)
                {
                    //if (GameContext.Current.TurnNumber >= 1)
                    scriptedEvent.OnTurnPhaseStarted(game, phase);
                }
            }

            TurnPhaseChanged?.Invoke(phase);
        }
        #endregion

        #region OnTurnPhaseFinished() Method
        /// <summary>
        /// Raises the <see cref="TurnPhaseChanged"/> event.
        /// </summary>
        /// /// <param name="game">The current game.</param>
        /// <param name="phase">The turn phase that just finished.</param>
        private void OnTurnPhaseFinished(GameContext game, TurnPhase phase)
        {
            if (phase != TurnPhase.SendUpdates)
            {
                foreach (Scripting.ScriptedEvent scriptedEvent in game.ScriptedEvents)
                {
                    if (GameContext.Current.TurnNumber > 2)
                        scriptedEvent.OnTurnPhaseFinished(game, phase);
                }
            }

            TurnPhaseFinished?.Invoke(phase);
        }
        #endregion

        #region OnFleetLocationChanged() Method
        /// <summary>
        /// Raises the <see cref="FleetLocationChanged"/> event.
        /// </summary>
        /// <param name="fleet">A Fleet whose Location just changed.</param>
        private void OnFleetLocationChanged(Fleet fleet)
        {
            FleetLocationChanged?.Invoke(this, new ParameterEventArgs<Fleet>(fleet));
        }
        #endregion

        #region DoTurn() Method
        /// <summary>
        /// Perform turn processing for the specified game context.
        /// </summary>
        /// <param name="game">The game context.</param>
        public void DoTurn([NotNull] GameContext game)
        {
            if (game == null)
                throw new ArgumentNullException("game");

            GameLog.Core.Events.DebugFormat("...beginning DoTurn...");

            HashSet<Fleet> fleets;

            GameContext.PushThreadContext(game);
            try
            {
                List<Scripting.ScriptedEvent> eventsToRemove = game.ScriptedEvents.Where(o => !o.CanExecute).ToList();
                foreach (Scripting.ScriptedEvent eventToRemove in eventsToRemove)
                    game.ScriptedEvents.Remove(eventToRemove);

                //Update If we've reached turn x, start running scripted events
                if (GameContext.Current.TurnNumber >= 1)
                {
                    foreach (Scripting.ScriptedEvent scriptedEvent in game.ScriptedEvents)
                    {
                        scriptedEvent.OnTurnStarted(game);
                    }
                }

                fleets = game.Universe.Find<Fleet>();

                foreach (Fleet fleet in fleets)
                    fleet.LocationChanged += HandleFleetLocationChanged;
            }
            finally { GameContext.PopThreadContext(); }

            GameLog.Core.Events.DebugFormat("...beginning PreTurnOperations...");

            OnTurnPhaseChanged(game, TurnPhase.PreTurnOperations);
            GameContext.PushThreadContext(game);
            try { DoPreTurnOperations(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.PreTurnOperations);

            GameLog.Core.Events.DebugFormat("...beginning SpyOperations...");

            //OnTurnPhaseChanged(game, TurnPhase.SpyOperations);
            //GameContext.PushThreadContext(game);
            //try { DoSpyOperations(); } //?? do we need game in the constructor ??
            //finally { GameContext.PopThreadContext(); }
            //OnTurnPhaseFinished(game, TurnPhase.SpyOperations);

            GameLog.Core.Events.DebugFormat("...beginning FleetMovement...");

            OnTurnPhaseChanged(game, TurnPhase.FleetMovement);
            GameContext.PushThreadContext(game);
            try { DoFleetMovement(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.FleetMovement);

            GameLog.Core.Events.DebugFormat("...beginning Diplomacy...");

            OnTurnPhaseChanged(game, TurnPhase.Diplomacy);
            GameContext.PushThreadContext(game);
            try { DoDiplomacy(); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Diplomacy);

            GameLog.Core.Events.DebugFormat("...beginning Combat...");

            OnTurnPhaseChanged(game, TurnPhase.Combat);
            GameContext.PushThreadContext(game);
            try { DoCombat(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Combat);

            GameLog.Core.Events.DebugFormat("...beginning PopulationGrowth...");

            OnTurnPhaseChanged(game, TurnPhase.PopulationGrowth);
            GameContext.PushThreadContext(game);
            try { DoPopulation(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.PopulationGrowth);

            GameLog.Core.Events.DebugFormat("...beginning Research...");

            OnTurnPhaseChanged(game, TurnPhase.Research);
            GameContext.PushThreadContext(game);
            try { DoResearch(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Research);

            GameLog.Core.Events.DebugFormat("...beginning Scrapping...");

            OnTurnPhaseChanged(game, TurnPhase.Scrapping);
            GameContext.PushThreadContext(game);
            try { DoScrapping(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Scrapping);

            GameLog.Core.Events.DebugFormat("...beginning Maintenance...");

            OnTurnPhaseChanged(game, TurnPhase.Maintenance);
            GameContext.PushThreadContext(game);
            try { DoMaintenance(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Maintenance);

            GameLog.Core.Events.DebugFormat("...beginning Production...");

            OnTurnPhaseChanged(game, TurnPhase.Production);
            GameContext.PushThreadContext(game);
            try { DoProduction(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Production);

            GameLog.Core.Events.DebugFormat("...beginning ShipProduction...");

            OnTurnPhaseChanged(game, TurnPhase.ShipProduction);
            GameContext.PushThreadContext(game);
            try { DoShipProduction(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.ShipProduction);

            GameLog.Core.Events.DebugFormat("...beginning Trade...");

            OnTurnPhaseChanged(game, TurnPhase.Trade);
            GameContext.PushThreadContext(game);
            try { DoTrade(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Trade);

            //GameLog.Core.Events.DebugFormat("...beginning Intelligence...");

            //OnTurnPhaseChanged(game, TurnPhase.Intelligence);
            //GameContext.PushThreadContext(game);
            //try { DoIntelligence(game); }
            //finally { GameContext.PopThreadContext(); }
            //OnTurnPhaseFinished(game, TurnPhase.Intelligence);

            GameLog.Core.Events.DebugFormat("...beginning Morale...");

            OnTurnPhaseChanged(game, TurnPhase.Morale);
            GameContext.PushThreadContext(game);
            try { DoMorale(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.Morale);

            GameLog.Core.Events.DebugFormat("...beginning MapUpdates...");

            OnTurnPhaseChanged(game, TurnPhase.MapUpdates);
            GameContext.PushThreadContext(game);
            try { DoMapUpdates(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.MapUpdates);

            GameLog.Core.Events.DebugFormat("...beginning PostTurnOperations...");

            OnTurnPhaseChanged(game, TurnPhase.PostTurnOperations);
            GameContext.PushThreadContext(game);
            try { DoPostTurnOperations(game); }
            finally { GameContext.PopThreadContext(); }
            OnTurnPhaseFinished(game, TurnPhase.PostTurnOperations);

            GameLog.Core.Events.DebugFormat("...beginning SendUpdates...");

            OnTurnPhaseChanged(game, TurnPhase.SendUpdates);

            GameLog.Core.Events.DebugFormat("...beginning PushThreadContext...");

            GameContext.PushThreadContext(game);
            try
            {
                foreach (Scripting.ScriptedEvent scriptedEvent in game.ScriptedEvents)
                {
                    //if (GameContext.Current.TurnNumber >= 50)
                    scriptedEvent.OnTurnFinished(game);
                }

            }
            finally { GameContext.PopThreadContext(); }


            GameLog.Core.Events.DebugFormat("...HandleFleetLocationChanged...");


            foreach (Fleet fleet in fleets)
                fleet.LocationChanged -= HandleFleetLocationChanged;
        }
        #endregion DoTurn

        #region HandleFleetLocationChanged() Method
        private void HandleFleetLocationChanged(object sender, EventArgs e)
        {
            Fleet fleet = sender as Fleet;
            if (fleet != null)
                OnFleetLocationChanged(fleet);
        }
        #endregion

        #region DoPreTurnOperations() Method
        private void DoPreTurnOperations(GameContext game)
        {
            HashSet<UniverseObject> objects = GameContext.Current.Universe.Objects.ToHashSet();
            HashSet<CivilizationManager> civManagers = GameContext.Current.CivilizationManagers.ToHashSet();
            HashSet<Fleet> fleets = objects.OfType<Fleet>().ToHashSet();
            ConcurrentStack<Exception> errors = new System.Collections.Concurrent.ConcurrentStack<Exception>();

            CivilizationKeyedMap<Diplomat> diplomatCheck = game.Diplomats;

            GameLog.Core.General.DebugFormat("resetting items...");
            ParallelForEach(objects, item =>
            {
                GameContext.PushThreadContext(game);
                // GameLog.Core.General.DebugFormat("next item will be: ID = {0}, Name = {1}", item.ObjectID, item.Name);
                try
                {
                    // GameLog.Core.General.DebugFormat("item: ID = {0}, Name = {1} is trying to reset", item.ObjectID, item.Name);
                    item.Reset();
                    // works well but gives hidden info
                    // GameLog.Core.General.DebugFormat("item: ID = {0}, Name = {1} is successfully resetted", item.ObjectID, item.Name);
                }
                catch (Exception e)
                {
                    GameLog.Core.General.ErrorFormat("***** catch error e item: ID = {0}, Name = {1}", item.ObjectID, item.Name);
                    errors.Push(e);
                }
                finally
                {
                    GameContext.PushThreadContext(game);
                }
            });

            if (!errors.IsEmpty)
                throw new AggregateException(errors);

            errors.Clear();

            ParallelForEach(civManagers, civManager =>
            {
                GameContext.PushThreadContext(game);
                civManager.SitRepEntries.Clear();
                try
                {
                    //civManager.SitRepEntries.Clear();

                    try
                    {
                        List<SitRepEntry> civSitReps = IntelHelper.SitReps_Temp.Where(o => o.Owner == civManager.Civilization).ToList();
                        foreach (SitRepEntry entry in civSitReps)
                        {
                            civManager.SitRepEntries.Add(entry);
                        }
                    }
                    catch (Exception e) { GameLog.Client.General.ErrorFormat("SitRep civManager error ={0}", e); }
                }
                catch (Exception e)
                {
                    errors.Push(e);
                    GameLog.Client.General.ErrorFormat("SitRepEntries clear error ={0}", e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });

            IntelHelper.SitReps_Temp.Clear();

            if (!errors.IsEmpty)
                throw new AggregateException(innerExceptions: errors);

            // This block is not guaranteed to be safe for parallel execution.
            GameContext.PushThreadContext(game);
            foreach (Fleet fleet in fleets)
            {
                if (fleet.Order != null)
                {
                    fleet.Order.OnTurnBeginning();
                }
            }
        }
        #endregion

        #region DoPreGameSetup() Method
        public void DoPreGameSetup(GameContext game)
        {
            ConcurrentStack<Exception> errors = new ConcurrentStack<Exception>();

            ParallelForEach(GameContext.Current.Civilizations, civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    if (!GameContext.Current.CivilizationManagers.Contains(civ.CivID))
                    {

                        GameContext.Current.CivilizationManagers.Add(new CivilizationManager(game, civ));
                        GameLog.Core.General.DebugFormat("New civ added: {0}", civ.Name);
                    }
                }
                catch (Exception e)
                {
                    errors.Push(e);
                }
                GameContext.PopThreadContext();
            });

            if (!errors.IsEmpty)
                throw new AggregateException(errors);

            DoMapUpdates(game);

            //GameLog.Print("GameVersion = {0}", GameContext.Current.GameMod.Version);
            GameLog.Core.General.InfoFormat("Options: ---------------------------");
            GameLog.Core.General.InfoFormat("Options: GalaxySize = {0} ({1} x {2})", GameContext.Current.Options.GalaxySize, GameContext.Current.Universe.Map.Width, GameContext.Current.Universe.Map.Height);
            GameLog.Core.GeneralDetails.DebugFormat("Options: GalaxyShape = {0}", GameContext.Current.Options.GalaxyShape);
            GameLog.Core.GeneralDetails.DebugFormat("Options: StarDensity = {0}", GameContext.Current.Options.StarDensity);
            GameLog.Core.GeneralDetails.DebugFormat("Options: PlanetDensity = {0}", GameContext.Current.Options.PlanetDensity);
            GameLog.Core.General.InfoFormat("Options: StartingTechLevel = {0}", GameContext.Current.Options.StartingTechLevel);
            GameLog.Core.GeneralDetails.DebugFormat("Options: MinorRaceFrequency = {0}", GameContext.Current.Options.MinorRaceFrequency);
            GameLog.Core.GeneralDetails.DebugFormat("Options: GalaxyCanon = {0}", GameContext.Current.Options.GalaxyCanon);
            GameLog.Core.GeneralDetails.DebugFormat("Options: ---------------------------");
            GameLog.Core.GeneralDetails.DebugFormat("Options: FederationPlayable = {0}", GameContext.Current.Options.FederationPlayable);
            GameLog.Core.GeneralDetails.DebugFormat("Options: RomulanPlayable = {0}", GameContext.Current.Options.RomulanPlayable);
            GameLog.Core.GeneralDetails.DebugFormat("Options: KlingonPlayable = {0}", GameContext.Current.Options.KlingonPlayable);
            GameLog.Core.GeneralDetails.DebugFormat("Options: CardassianPlayable = {0}", GameContext.Current.Options.CardassianPlayable);
            GameLog.Core.GeneralDetails.DebugFormat("Options: DominionPlayable = {0}", GameContext.Current.Options.DominionPlayable);
            GameLog.Core.GeneralDetails.DebugFormat("Options: BorgPlayable = {0}", GameContext.Current.Options.BorgPlayable);
            GameLog.Core.GeneralDetails.DebugFormat("Options: TerranEmpirePlayable = {0}", GameContext.Current.Options.TerranEmpirePlayable);
            GameLog.Core.GeneralDetails.DebugFormat("Options: ---------------------------");
            GameLog.Core.GeneralDetails.DebugFormat("Options: FederationModifier = {0}", GameContext.Current.Options.FederationModifier);
            GameLog.Core.GeneralDetails.DebugFormat("Options: RomulanModifier = {0}", GameContext.Current.Options.RomulanModifier);
            GameLog.Core.GeneralDetails.DebugFormat("Options: KlingonModifier = {0}", GameContext.Current.Options.KlingonModifier);
            GameLog.Core.GeneralDetails.DebugFormat("Options: CardassianModifier = {0}", GameContext.Current.Options.CardassianModifier);
            GameLog.Core.GeneralDetails.DebugFormat("Options: DominionModifier = {0}", GameContext.Current.Options.DominionModifier);
            GameLog.Core.GeneralDetails.DebugFormat("Options: BorgModifier = {0}", GameContext.Current.Options.BorgModifier);
            GameLog.Core.GeneralDetails.DebugFormat("Options: TerranEmpireModifier = {0}", GameContext.Current.Options.TerranEmpireModifier);

            GameLog.Core.GeneralDetails.DebugFormat("Options: EmpireModifierRecurringBalancing = {0}", GameContext.Current.Options.EmpireModifierRecurringBalancing);
            GameLog.Core.GeneralDetails.DebugFormat("Options: GamePace = {0}", GameContext.Current.Options.GamePace);
            GameLog.Core.GeneralDetails.DebugFormat("Options: TurnTimer = {0}", GameContext.Current.Options.TurnTimerEnum);

            Table ToolTipImageSizeTable = GameContext.Current.Tables.UniverseTables["Sizes"];
            AAASpecialWidth1 = (int)Number.ParseSingle(ToolTipImageSizeTable["Width"][0]);
            AAASpecialHeight1 = (int)Number.ParseSingle(ToolTipImageSizeTable["Height"][0]);
            string _text = "AAASpecialWidth1=" + AAASpecialWidth1 + " x " + "AAASpecialHeight1=" + AAASpecialHeight1;
            Console.WriteLine(_text);
            GameLog.Core.GeneralDetails.DebugFormat(_text);




            /* With StrengthModifier it is possible to increase some stuff or to decrease */
            /* default value is 1.0 - range shall be 0.1 to 1.9 */
            /* all modifier are working in generell, not race-speficic */
            Table strengthTable = GameContext.Current.Tables.StrengthTables["StrengthModifier"];
            float EspionageMod = Number.ParseSingle(strengthTable["EspionageMod"][0]);
            float SabotageMod = Number.ParseSingle(strengthTable["SabotageMod"][0]);
            float InternalSecurityMod = Number.ParseSingle(strengthTable["InternalSecurityMod"][0]);
            float ShipProductionMod = Number.ParseSingle(strengthTable["ShipProductionMod"][0]);
            float ScienceSpeedMod = Number.ParseSingle(strengthTable["ScienceSpeedMod"][0]);
            float MinorPowerMod = Number.ParseSingle(strengthTable["MinorPowerMod"][0]);
            float MiningMod = Number.ParseSingle(strengthTable["MiningMod"][0]);
            float CreditsMod = Number.ParseSingle(strengthTable["CreditsMod"][0]);
            float DiplomacyTrustMod = Number.ParseSingle(strengthTable["DiplomacyTrustMod"][0]);
            float DiplomacyRegardMod = Number.ParseSingle(strengthTable["DiplomacyRegardMod"][0]);
            float FoodProductionMod = Number.ParseSingle(strengthTable["FoodProductionMod"][0]);
            float RaidingMod = Number.ParseSingle(strengthTable["RaidingMod"][0]);
            float ShipVisibilityMod = Number.ParseSingle(strengthTable["ShipVisibilityMod"][0]);
            float StationsStrenghtMod = Number.ParseSingle(strengthTable["StationsStrenghtMod"][0]);
            float OrbitalBatteryStrenghtMod = Number.ParseSingle(strengthTable["OrbitalBatteryStrenghtMod"][0]);
            float TroopTransportStrenghtMod = Number.ParseSingle(strengthTable["TroopTransportStrenghtMod"][0]);
            float ColonyTroopStrenghtMod = Number.ParseSingle(strengthTable["ColonyTroopStrenghtMod"][0]);

            string newline = Environment.NewLine;
            GameLog.Core.GameInitDataDetails.DebugFormat("StrengthModifier: " + newline +
                "EspionageMod = {0}" + newline +
                "SabotageMod = {1}" + newline +
                "InternalSecurityMod = {2}" + newline +
                "ShipProductionMod = {3}" + newline +
                "ScienceSpeedMod = {4}" + newline +
                "MinorPowerMod = {5}" + newline +
                "MiningMod = {6}" + newline +
                "CreditsMod = {7}" + newline +
                "DiplomacyTrustMod = {8}" + newline +
                "DiplomacyRegardMod = {9}" + newline +
                "FoodProductionMod = {10}" + newline +
                "RaidingMod = {11}" + newline +
                "ShipVisibilityMod = {12}" + newline +
                "StationsStrenghtMod = {13}" + newline +
                "OrbitalBatteryStrenghtMod = {14}" + newline +
                "TroopTransportStrenghtMod = {15}" + newline +
                "ColonyTroopStrenghtMod = {16}" + newline
                , EspionageMod
                , SabotageMod
                , InternalSecurityMod
                , ShipProductionMod
                , ScienceSpeedMod
                , MinorPowerMod
                , MiningMod
                , CreditsMod
                , DiplomacyTrustMod
                , DiplomacyRegardMod
                , FoodProductionMod
                , RaidingMod
                , ShipVisibilityMod
                , StationsStrenghtMod
                , OrbitalBatteryStrenghtMod
                , TroopTransportStrenghtMod
                , ColonyTroopStrenghtMod
                );



            game.TurnNumber = 1;
            _gameTurnNumber = 1;

        }
        #endregion

        #region DoFleetMovement() Method
        private void DoFleetMovement(GameContext game)
        {
            List<Fleet> allFleets = GameContext.Current.Universe.Find<Fleet>().ToList();


            foreach (Fleet fleet in allFleets)
            {
                string _text;
                int shipNum = fleet.Ships.Count();
                //string name = fleet.Name;
                if (fleet.UnitAIType == UnitAIType.Reserve)
                    GameLog.Client.AI.DebugFormat("*** Reserve, Turn# = {0} Owner = {1} Fleet location ={2}, UnitAIType ={3}, UnitActivity ={4} Actibvity Duration ={5} Activity Start ={6}",
                        GameContext.Current.TurnNumber, fleet.Owner.Name, fleet.Location, fleet.UnitAIType, fleet.Activity, fleet.ActivityDuration, fleet.ActivityStart);
                if (shipNum >= 5 && fleet.UnitAIType != UnitAIType.SystemDefense)
                    GameLog.Client.AI.DebugFormat("*** >5 and Not SystemDefence, Turn# = {0} Owner = {1} Fleet location ={2}, UnitAIType ={3}, UnitActivity ={4} Actibvity Duration ={5} Activity Start ={6}",
                        GameContext.Current.TurnNumber, fleet.Owner.Name, fleet.Location, fleet.UnitAIType, fleet.Activity, fleet.ActivityDuration, fleet.ActivityStart);
                if (fleet.Route.Steps.Count() > 0 && shipNum >= 5)
                {
                    GameLog.Client.AI.DebugFormat("# of ships = {0} fleet Waypooints ={1} step 1 {2}", shipNum, fleet.Route.Waypoints, fleet.Route.Steps[0]);
                    for (int i = 0; i < shipNum; i++)
                    {
                        _text = "Ship's Fleet# =" + i;
                        Ship ship = fleet.Ships[i];
                        _text += "/ObjectID =" + ship.ObjectID + "/Name =" + ship.Name;
                        GameLog.Core.AI.DebugFormat(_text);
                    }
                }

                //If the fleet is stranded and out of fuel range, it can't move
                if (fleet.IsStranded && !fleet.IsFleetInFuelRange())
                {
                    if (fleet.IsRouteLocked)
                        fleet.UnlockRoute();

                    fleet.SetRoute(TravelRoute.Empty);
                }

                CivilizationManager civManager = GameContext.Current.CivilizationManagers[fleet.Owner];
                int fuelNeeded;
                int fuelRange = civManager.MapData.GetFuelRange(fleet.Location);

                /*
                 * If the fleet is within fueling range, then try to top off the reserves of
                 * each ship in the fleet.  We do this now in case a ship is out of fuel, but
                 * is now within fueling range, thus ensuring the ship will be able to move.
                 */
                if (!fleet.IsInTow && (fleet.Range >= fuelRange))
                {
                    foreach (Ship ship in fleet.Ships)
                    {
                        fuelNeeded = ship.FuelReserve.Maximum - ship.FuelReserve.CurrentValue;

                        ship.FuelReserve.AdjustCurrent(
                            civManager.Resources[ResourceType.Deuterium].AdjustCurrent(-fuelNeeded));
                    }
                }

                //Move the ships along their route
                for (int i = 0; i < fleet.Speed; i++)
                {
                    if (fleet.MoveAlongRoute())
                    {
                        fleet.AdjustCrewExperience(5);
                    }
                    else
                    {
                        if (i == 0)
                            fleet.AdjustCrewExperience(1);
                        break;
                    }

                    fuelRange = civManager.MapData.GetFuelRange(fleet.Location);

                    foreach (Ship ship in fleet.Ships)
                    {
                        /*
                         * For each ship in the fleet, deplete the fuel reserves by a 1 unit
                         * of Deuterium.  Then, if the fleet is within fueling range, attempt
                         * to replenish that unit from the global stockpile.
                         */
                        fuelNeeded = ship.FuelReserve.AdjustCurrent(-1);

                        if (fleet.Range >= fuelRange)
                        {
                            ship.FuelReserve.AdjustCurrent(
                                civManager.Resources[ResourceType.Deuterium].AdjustCurrent(-fuelNeeded));
                        }
                        //ship.FuelReserve.UpdateAndReset();
                    }
                }

                if (fleet.Sector.System != null && (fleet.Sector.System.StarType == StarType.BlackHole))
                {
                    int shipsDamaged = 0;
                    int shipsDestroyed = 0;

                    if (fleet.Ships != null) // Update FixBlackholeCrash (hopefully) 2 March 2019
                    {

                        foreach (Ship ship in fleet.Ships)
                        {
                            int damage = RandomHelper.Roll(ship.HullStrength.CurrentValue);
                            if (damage >= ship.HullStrength.CurrentValue)
                            {
                                shipsDestroyed++;
                                ship.Destroy();
                            }
                            else
                            {
                                shipsDamaged++;
                                ship.HullStrength.AdjustCurrent(-damage);
                            }
                            if (fleet.Ships != null) // 2nd try to fix blackhole 3 march 2019
                                break;
                        }

                    }


                    if ((shipsDamaged > 0) || (shipsDestroyed > 0))
                    {
                        GameLog.Client.AI.DebugFormat("shipDestroyed {0} Ship(s) went down a Black hole {1} {2}", shipsDestroyed, fleet.Owner.Key, fleet.Location);
                        civManager.SitRepEntries.Add(new BlackHoleEncounterSitRepEntry(fleet.Owner, fleet.Location, shipsDamaged, shipsDestroyed));
                    }
                }
            }
        }
        #endregion

        #region DoDiplomacy() Method
        private void DoDiplomacy()
        {
            //DiplomacyHelper.ClearAcceptRejectDictionary();
            CivilizationManagerMap civManagers = GameContext.Current.CivilizationManagers;

            // FIRST: Pending Actions
            foreach (Civilization civ1 in GameContext.Current.Civilizations)
            {
                if (civ1.IsHuman)
                    DiplomacyHelper.AcceptingRejecting(civ1);
                foreach (Civilization civ2 in GameContext.Current.Civilizations)
                {
                    if (civ1 == civ2)
                        continue;
                    bool _itIsBorg = false;
                    if (civ1.CivID == 6 || civ1.Key == "BORG")
                    {
                        _itIsBorg = true;
                        //GameLog.Core.Diplomacy.DebugFormat("civ1 = {0}, civ2 = {1}, foreignPower = {2}, foreignPowerStatus = {3}", civ1, civ2, foreignPower, foreignPowerStatus);
                    }
                    if (civ2.CivID == 6 || civ2.Key == "BORG")
                    {
                        _itIsBorg = true;

                    }
                    Diplomat diplomat1 = Diplomat.Get(civ1);

                    Diplomat diplomat2 = Diplomat.Get(civ2);
                    if (diplomat1.GetForeignPower(civ2).DiplomacyData.Status == Diplomacy.ForeignPowerStatus.NoContact ||
                        diplomat2.GetForeignPower(civ1).DiplomacyData.Status == Diplomacy.ForeignPowerStatus.NoContact)
                    {
                        //GameLog.Core.Diplomacy.DebugFormat("DiplomacyData.Status = NoContact for {0} vs {1}", civ1, civ2);
                        continue;
                    }

                    ForeignPower foreignPower = diplomat1.GetForeignPower(civ2);
                    ForeignPowerStatus foreignPowerStatus = diplomat1.GetForeignPower(civ2).DiplomacyData.Status;
                    if (_itIsBorg == true)
                    {
                        if (civ1.CivID == 6 || civ1.Key == "BORG")
                        {
                            //var aForeignPower = diplomat1.GetForeignPower(civ2);
                            DiplomacyHelper.ApplyTrustChange(foreignPower.Counterparty, foreignPower.Owner, -1000);
                            DiplomacyHelper.ApplyRegardChange(foreignPower.Counterparty, foreignPower.Owner, -1000);
                        }
                        if (civ2.CivID == 6 || civ2.Key == "BORG")
                        {
                            DiplomacyHelper.ApplyTrustChange(foreignPower.Counterparty, foreignPower.Owner, -1000);
                            DiplomacyHelper.ApplyRegardChange(foreignPower.Counterparty, foreignPower.Owner, -1000);
                        }
                        continue;
                    }
                    if (foreignPowerStatus == ForeignPowerStatus.AtWar)
                    {
                        DiplomacyHelper.ApplyTrustChange(foreignPower.Counterparty, foreignPower.Owner, -1000);
                        DiplomacyHelper.ApplyRegardChange(foreignPower.Counterparty, foreignPower.Owner, -1000);
                    }

                    //GameLog.Core.Diplomacy.DebugFormat("---------------------------------------");
                    //GameLog.Core.Diplomacy.DebugFormat("foreignPowerStatus = {2} for {0} vs {1}", civ1, civ2, foreignPowerStatus.ToString());

                    switch (foreignPower.PendingAction)
                    {
                        case PendingDiplomacyAction.AcceptProposal:
                            {
                                GameLog.Core.Diplomacy.DebugFormat("$$ Accept Status = {2} for {0} vs {1}"
                                    , civ1
                                    , civ2
                                    , foreignPower.PendingAction.ToString());

                                if (foreignPower.ProposalReceived != null)
                                    AcceptProposalVisitor.Visit(foreignPower.ProposalReceived);

                                foreignPower.LastProposalReceived = foreignPower.ProposalReceived;
                                foreignPower.ProposalReceived = null;
                                break;
                            }

                        case PendingDiplomacyAction.RejectProposal:
                            {
                                GameLog.Core.Diplomacy.DebugFormat("$$ Reject Status = {2} for {0} vs {1}"
                                    , civ1
                                    , civ2
                                    , foreignPower.PendingAction.ToString());

                                if (foreignPower.ProposalReceived != null)
                                    RejectProposalVisitor.Visit(foreignPower.ProposalReceived);

                                foreignPower.LastProposalReceived = foreignPower.ProposalReceived;
                                foreignPower.ProposalReceived = null;
                                break;
                            }
                        default:
                            break;
                    }
                    //GameLog.Core.Diplomacy.DebugFormat("Next: foreignPower.PendingAction = NONE for {0} vs {1}, status {2}, pending {3}", foreignPower.Owner, foreignPower.Counterparty, foreignPowerStatus.ToString(), foreignPower.PendingAction.ToString());
                    foreignPower.PendingAction = PendingDiplomacyAction.None;

                    // Ships gets new owner on joining empire - colonies are done in AccpetPropsalVisitor
                    if (civ1.IsEmpire && !civ2.IsEmpire && civ1.Key != "Borg")
                    {
                        Diplomat currentDiplomat = Diplomat.Get(civ1);
                        if (currentDiplomat.GetForeignPower(civ2).DiplomacyData.Status == Diplomacy.ForeignPowerStatus.CounterpartyIsMember)
                        {
                            List<UniverseObject> _objectsCiv2 = GameContext.Current.Universe.Objects.Where(s => s.Owner == civ2)
                                    .Where(s => s.ObjectType == UniverseObjectType.Ship).ToList();
                            foreach (UniverseObject minorsObject in _objectsCiv2)
                            {
                                if (minorsObject.Owner == civ2)
                                {
                                    CivilizationManager targetMinor = GameContext.Current.CivilizationManagers[civ2];
                                    Colony minorCivHome = targetMinor.HomeColony;
                                    int gainedResearchPoints = minorCivHome.NetResearch;
                                    Ship ship = (Ship)minorsObject;
                                    ship.Owner = civ1;
                                    Fleet newfleet = ship.CreateFleet();
                                    newfleet.Owner = civ1;
                                    newfleet.SetOrder(FleetOrders.EngageOrder.Create());
                                    if (newfleet.Order == null)
                                    {
                                        newfleet.SetOrder(FleetOrders.AvoidOrder.Create());
                                    }
                                    ship.Scrap = false;
                                    GameContext.Current.CivilizationManagers[civ1].Research.UpdateResearch(gainedResearchPoints);

                                    //GameLog.Core.Ships.DebugFormat("Ship Joined:{0} {1}, Owner {2}, OwnerID {3}, Fleet.OwnerID {4}, Order {5} fleet name {6} gainedResearchPoints {7}",
                                    //        ship.ObjectID, ship.Name, ship.Owner, ship.OwnerID, newfleet.OwnerID, newfleet.Order, newfleet.Name, gainedResearchPoints);
                                }
                            }
                        }
                    }
                }
            }

            /*
            // Second: Schedule delivery of outbound messages  Including Statementreceived
             */
            GameLog.Core.Diplomacy.DebugFormat("NEXT: *Second* Outgoing");
            foreach (Civilization civ1 in GameContext.Current.Civilizations)
            {
                Diplomat diplomat = Diplomat.Get(civ1);

                foreach (Civilization civ2 in GameContext.Current.Civilizations)
                {
                    if (civ1 == civ2)
                        continue;

                    ForeignPower foreignPower = diplomat.GetForeignPower(civ2);
                    string _gameLog = "";

                    // just for testing especially generating break point
                    //if (civ1.CivID == 1 && civ2.CivID == 4 || civ1.CivID == 4 && civ2.CivID == 1)  // Terrans, incoming from Cardassians
                    //{
                    //_gameLog = "### Checking ForeignerPower - see next line";

                    #region Gamelogs
                    if (foreignPower.ProposalReceived != null)
                        _gameLog += Environment.NewLine + "ProposalReceived: "
                                  + foreignPower.ProposalReceived.Sender + " to "
                                  + foreignPower.ProposalReceived.Recipient + ": > "
                                  + foreignPower.ProposalReceived.Clauses.ToString()
                                  + Environment.NewLine;
                    if (foreignPower.ProposalSent != null)
                        _gameLog += Environment.NewLine + "ProposalSent: "
                                  + foreignPower.ProposalSent.Sender + " to "
                                  + foreignPower.ProposalSent.Recipient + ": > "
                                  + foreignPower.ProposalSent.Clauses.ToString()
                                  + Environment.NewLine;
                    if (foreignPower.ResponseReceived != null)
                        _gameLog += Environment.NewLine + "ResponseReceived: "
                                  + foreignPower.ResponseReceived.Sender + " to "
                                  + foreignPower.ResponseReceived.Recipient + ": > "
                                  + foreignPower.ResponseReceived.ResponseType.ToString()
                                  + Environment.NewLine;
                    if (foreignPower.ResponseSent != null)
                        _gameLog += Environment.NewLine + "ResponseSent: "
                                  + foreignPower.ResponseSent.Sender + " to "
                                  + foreignPower.ResponseSent.Recipient + ": > "
                                  + foreignPower.ResponseSent.ResponseType.ToString()
                                  + Environment.NewLine;
                    if (foreignPower.StatementReceived != null)  // in SinglePlayer you'll never get this "received" because you are always the playing SENDER unitl AI sends
                    {

                        //string parameterString = foreignPower.StatementSent.Parameter.ToString() ?? "";

                        _gameLog += Environment.NewLine + "StatementReceived: "
                                  + foreignPower.StatementReceived.Sender + " to "
                                  + foreignPower.StatementReceived.Recipient + ": > "
                                  + ", Parameter = " //+ parameterString
                                  + Enum.GetName(typeof(StatementType), foreignPower.StatementReceived.StatementType)
                                  + Environment.NewLine
                                  ;
                    }
                    if (foreignPower.StatementSent != null)  // in SinglePlayer you'll never get this "received" because you are always the playing SENDER unitl AI sends
                    {

                        //string parameterString = foreignPower.StatementSent.Parameter.ToString() ?? "";

                        _gameLog += Environment.NewLine + "StatementSent: "
                                  + foreignPower.StatementSent.Sender + " to "
                                  + foreignPower.StatementSent.Recipient + ": > "
                                  + ", Parameter = " //+ parameterString
                                  + Environment.NewLine
                                  ;
                    }

                    // GameLog.Core.Diplomacy.DebugFormat("------------------------------------------");
                    //GameLog.Core.Diplomacy.DebugFormat("received a 'Sabotage'-Diplomacy-Statement, Tone = {0}", foreignPower.StatementReceived.Tone.ToString());

                    if (_gameLog.Length > 44)  // not only the entry phrase...
                        GameLog.Core.Diplomacy.DebugFormat(_gameLog);
                    #endregion Gamelogs
                    //}

                    //  Second.1 = StatementReceived
                    if (foreignPower.StatementReceived != null)
                    {
                        switch (foreignPower.StatementReceived.StatementType)
                        {
                            case StatementType.StealCredits:
                                if (civ2.CivID > civ1.CivID)
                                    IntelHelper.SabotageStealCreditsExecute(civ2, civ1, foreignPower.StatementReceived.Parameter.ToString(), 99999);
                                break;
                            case StatementType.StealResearch:
                                if (civ2.CivID > civ1.CivID)
                                    IntelHelper.SabotageStealResearchExecute(civ2, civ1, foreignPower.StatementReceived.Parameter.ToString(), 99999);
                                break;
                            case StatementType.SabotageFood:
                                if (civ2.CivID > civ1.CivID)
                                    IntelHelper.SabotageFoodExecute(civ2, civ1, foreignPower.StatementReceived.Parameter.ToString(), 99999);
                                break;
                            case StatementType.SabotageIndustry:
                                if (civ2.CivID > civ1.CivID)
                                    IntelHelper.SabotageIndustryExecute(civ2, civ1, foreignPower.StatementReceived.Parameter.ToString(), 99999);
                                break;
                            case StatementType.SabotageEnergy:
                                if (civ2.CivID > civ1.CivID)
                                    IntelHelper.SabotageEnergyExecute(civ2, civ1, foreignPower.StatementReceived.Parameter.ToString(), 99999);
                                break;

                            case StatementType.T01: // read statement type off of foreignPower and send it to accept - reject dictionary
                            case StatementType.T02:
                            case StatementType.T03:
                            case StatementType.T04:
                            case StatementType.T05:
                            case StatementType.T10:
                            case StatementType.T12:
                            case StatementType.T13:
                            case StatementType.T14:
                            case StatementType.T15:
                            case StatementType.T20:
                            case StatementType.T21:
                            case StatementType.T23:
                            case StatementType.T24:
                            case StatementType.T25:
                            case StatementType.T30:
                            case StatementType.T31:
                            case StatementType.T32:
                            case StatementType.T34:
                            case StatementType.T35:
                            case StatementType.T40:
                            case StatementType.T41:
                            case StatementType.T42:
                            case StatementType.T43:
                            case StatementType.T45:
                            case StatementType.T50:
                            case StatementType.T51:
                            case StatementType.T52:
                            case StatementType.T53:
                            case StatementType.T54:
                            case StatementType.F01:
                            case StatementType.F02:
                            case StatementType.F03:
                            case StatementType.F04:
                            case StatementType.F05:
                            case StatementType.F10:
                            case StatementType.F12:
                            case StatementType.F13:
                            case StatementType.F14:
                            case StatementType.F15:
                            case StatementType.F20:
                            case StatementType.F21:
                            case StatementType.F23:
                            case StatementType.F24:
                            case StatementType.F25:
                            case StatementType.F30:
                            case StatementType.F31:
                            case StatementType.F32:
                            case StatementType.F34:
                            case StatementType.F35:
                            case StatementType.F40:
                            case StatementType.F41:
                            case StatementType.F42:
                            case StatementType.F43:
                            case StatementType.F45:
                            case StatementType.F50:
                            case StatementType.F51:
                            case StatementType.F52:
                            case StatementType.F53:
                            case StatementType.F54:
                                {
                                    GameLog.Core.Diplomacy.DebugFormat("Statement sent for Dictionary Entery {0} foreignPower Counterparty {1}, Owner {2}",
                                        Enum.GetName(typeof(StatementType), foreignPower.StatementReceived.StatementType),
                                        foreignPower.Counterparty.Key,
                                        foreignPower.Owner.Key);

                                    DiplomacyHelper.SpecificCivAcceptingRejecting(foreignPower.StatementReceived.StatementType); // act on statement to accept reject
                                    break;
                                }
                            case StatementType.WarPact:
                            case StatementType.CommendWar:
                            case StatementType.DenounceWar:
                            case StatementType.WarDeclaration:
                                break;
                            default:
                                break;
                        }
                    }
                    else

                    //  Second.1 = StatementReceived
                    if (foreignPower.LastStatementReceived != null)
                    {
                        switch (foreignPower.LastStatementReceived.StatementType)
                        {
                            case StatementType.StealCredits:
                                IntelHelper.SabotageStealCreditsExecute(civ2, civ1, foreignPower.LastStatementReceived.Parameter.ToString(), 99999);
                                foreignPower.LastStatementReceived = null;
                                break;
                            case StatementType.StealResearch:
                                IntelHelper.SabotageStealResearchExecute(civ2, civ1, foreignPower.LastStatementReceived.Parameter.ToString(), 99999);
                                foreignPower.LastStatementReceived = null;
                                break;
                            case StatementType.SabotageFood:
                                IntelHelper.SabotageFoodExecute(civ2, civ1, foreignPower.LastStatementReceived.Parameter.ToString(), 99999);
                                foreignPower.LastStatementReceived = null;
                                break;
                            case StatementType.SabotageIndustry:
                                IntelHelper.SabotageIndustryExecute(civ2, civ1, foreignPower.LastStatementReceived.Parameter.ToString(), 99999);
                                foreignPower.LastStatementReceived = null;
                                break;
                            case StatementType.SabotageEnergy:
                                IntelHelper.SabotageEnergyExecute(civ2, civ1, foreignPower.LastStatementReceived.Parameter.ToString(), 99999);
                                foreignPower.LastStatementReceived = null;
                                break;
                            //    GameLog.Core.Diplomacy.DebugFormat("LastStatementReceived Statement Type = {0} foreignPower counterparyt {1}, owner {2}",
                            //        Enum.GetName(typeof(StatementType), foreignPower.LastStatementReceived.StatementType),
                            //        foreignPower.Counterparty.Key,
                            //        foreignPower.Owner.Key);
                            //    //DiplomacyHelper.AcceptRejectDictionaryFromStatement(foreignPower.LastStatementReceived);
                            //    DiplomacyHelper.SpecificCivAcceptingRejecting(foreignPower.LastStatementReceived.StatementType);
                            //    break;
                            case StatementType.CommendWar:
                            case StatementType.DenounceWar:
                            case StatementType.WarDeclaration:
                                break;
                            default:
                                break;
                        }
                    }

                    _gameLog = "what's next + ";

                    if (foreignPower.StatementSent != null)
                        _gameLog += Environment.NewLine + "(relevant is just the receive on HOSTING side.... StatementSent: "
                                    + foreignPower.StatementSent.Sender + " vs "
                                    + foreignPower.StatementSent.Recipient + ": > "
                                    + foreignPower.StatementSent.StatementType.ToString()
                                    + ", Parameter = " //+ parameterString
                                    + Environment.NewLine;

                    if (foreignPower.PendingAction != PendingDiplomacyAction.None)
                        _gameLog += Environment.NewLine + "PendingAction: "
                                    //+ foreignPower.PendingAction + " vs "
                                    //+ foreignPower.PendingAction.Recipient
                                    + foreignPower.PendingAction.ToString()
                                    + Environment.NewLine;

                    if (_gameLog != "what's next + ")
                        GameLog.Core.Diplomacy.DebugFormat(_gameLog);

                    //  Second.2 = proposalSent
                    IProposal proposalSent = foreignPower.ProposalSent;
                    if (proposalSent != null)
                    {
                        foreignPower.CounterpartyForeignPower.ProposalReceived = proposalSent;
                        foreignPower.LastProposalSent = proposalSent;
                        foreignPower.ProposalSent = null;
                        GameLog.Client.Diplomacy.DebugFormat("** ProposalSent becomes Counterparty ProposalReceived [{0}], Counterparty = {1}, Owner = {2}"
                            , foreignPower.LastProposalSent.Clauses[0].ClauseType.ToString(), foreignPower.Counterparty.ToString(), foreignPower.Owner.ToString()); ;

                        if (civ1.IsEmpire)
                            civManagers[civ1].SitRepEntries.Add(new DiplomaticSitRepEntry(civ1, proposalSent));

                        if (civ2.IsEmpire)
                            civManagers[civ2].SitRepEntries.Add(new DiplomaticSitRepEntry(civ2, proposalSent));
                    }
                    else
                    {
                        foreignPower.CounterpartyForeignPower.ProposalReceived = null;
                    }

                    //  Second.3 = statementSent

                    Statement statementSent = foreignPower.StatementSent;
                    if (statementSent != null)
                    {
                        // StatementSent becomes counterparty StatementReceived
                        foreignPower.CounterpartyForeignPower.StatementReceived = statementSent;
                        GameLog.Client.Diplomacy.DebugFormat("foreignPower.Owner {0} got StatementReceived {1} from {2}"
                            , foreignPower.CounterpartyForeignPower.Owner.Key
                            , Enum.GetName(typeof(StatementType), statementSent.StatementType)
                            , statementSent.Sender.Key);
                        foreignPower.LastStatementSent = statementSent;
                        foreignPower.StatementSent = null;

                        //GameLog.Core.Diplomacy.DebugFormat("foreignPower.Owner = {0}", foreignPower.Owner.Key);
                        //GameLog.Core.Diplomacy.DebugFormat("CounterpartyForeignPower.Owner = {0}", foreignPower.CounterpartyForeignPower.Owner.Key);

                        if (statementSent.StatementType == StatementType.WarDeclaration)
                            foreignPower.DeclareWar();

                    }
                    else
                    {
                        foreignPower.CounterpartyForeignPower.StatementReceived = null;
                    }

                    //  Second.4 = responseSent
                    IResponse responseSent = foreignPower.ResponseSent;
                    if (responseSent != null)
                    {
                        foreignPower.CounterpartyForeignPower.ResponseReceived = responseSent; // cross over response sent to response received
                        GameLog.Client.Diplomacy.DebugFormat("{0} sent Response {1} to {2}"
                            , foreignPower.Owner.Key, foreignPower.ResponseSent.Proposal.ToString(), foreignPower.Counterparty.Key);
                        foreignPower.LastResponseSent = responseSent;
                        GameLog.Client.Diplomacy.DebugFormat("Response Sent stored in LastResponseSent, {0}", foreignPower.ResponseSent.ToString());
                        foreignPower.ResponseSent = null;

                        if (responseSent.ResponseType != ResponseType.NoResponse &&
                            !(responseSent.ResponseType == ResponseType.Accept && responseSent.Proposal.IsGift()))
                        {
                            if (civ1.IsEmpire)
                                civManagers[civ1].SitRepEntries.Add(new DiplomaticSitRepEntry(civ1, responseSent));

                            if (civ2.IsEmpire)
                                civManagers[civ2].SitRepEntries.Add(new DiplomaticSitRepEntry(civ2, responseSent));
                        }
                        else if (responseSent.ResponseType != ResponseType.NoResponse && responseSent.ResponseType == ResponseType.Reject)
                        {
                            if (civ1.IsEmpire)
                                civManagers[civ1].SitRepEntries.Add(new DiplomaticSitRepEntry(civ1, responseSent));

                            if (civ2.IsEmpire)
                                civManagers[civ2].SitRepEntries.Add(new DiplomaticSitRepEntry(civ2, responseSent));
                        }
                    }
                    else
                    {
                        foreignPower.CounterpartyForeignPower.ResponseReceived = null;
                    }
                }
            }

            /*
            // Third: Fulfill agreement obligations
             */
            foreach (IAgreement agreement in GameContext.Current.AgreementMatrix)
                AgreementFulfillmentVisitor.Visit(agreement);

        }

        #endregion

        #region DoCombat() Method
        void DoCombat(GameContext game)
        {
            HashSet<MapLocation> combatLocations = new HashSet<MapLocation>();
            HashSet<MapLocation> invasionLocations = new HashSet<MapLocation>();
            List<List<CombatAssets>> combats = new List<List<CombatAssets>>();
            List<InvasionArena> invasions = new List<InvasionArena>();
            List<Fleet> fleetsAtLocation = new List<Fleet>(GameContext.Current.Universe.Find<Fleet>(UniverseObjectType.Fleet)).ToList();

            foreach (Fleet fleet in fleetsAtLocation)
            {
                if (!combatLocations.Contains(fleet.Location))
                {
                    List<CombatAssets> assets = CombatHelper.GetCombatAssets(fleet.Location); // part of altering collection while using from GameEngine 216 and CombatHelper.cs line 58
                    List<Civilization> fleetsOwners = fleetsAtLocation
                            .Select(o => o.Owner)
                            .Distinct()
                            .ToList();

                    if (assets.Count > 1 && fleetsOwners.Count > 1)
                    {
                        foreach (Fleet nextFleet in fleetsAtLocation)
                            if (fleet.Owner == nextFleet.Owner ||
                                CombatHelper.WillFightAlongside(fleet.Owner, nextFleet.Owner) ||
                                !CombatHelper.WillEngage(fleet.Owner, nextFleet.Owner))
                                continue;
                        combats.Add(assets); // we add all the ships at this location if there is any combat. Combat decides who is in and on what side
                        combatLocations.Add(fleet.Location);
                    }
                }
                if (!invasionLocations.Contains(fleet.Location))
                {
                    if (fleet.Order is AssaultSystemOrder)
                    {
                        invasions.Add(new InvasionArena(fleet.Sector.System.Colony, fleet.Owner));
                        invasionLocations.Add(fleet.Location);
                    }
                }
            }

            foreach (List<CombatAssets> combat in combats)
            {
                CombatReset.Reset();
                GameLog.Core.Combat.DebugFormat("---- COMBAT OCCURED GameEngine --------------------");
                OnCombatOccurring(combat);
                CombatReset.WaitOne();

            }

            foreach (InvasionArena invasion in invasions)
            {
                CombatReset.Reset();
                OnInvasionOccurring(invasion);
                if (invasion.Invader.IsHuman)
                    CombatReset.WaitOne();
            }

            List<Fleet> invadingFleets = invasions
                .SelectMany(o => o.InvadingUnits)
                .OfType<InvasionOrbital>()
                .Where(o => !o.IsDestroyed)
                .Select(o => o.Source)
                .OfType<Ship>()
                .Select(o => o.Fleet)
                .Distinct()
                .ToList();

            foreach (Fleet invadingFleet in invadingFleets)
            {
                if (invadingFleet.Order is AssaultSystemOrder assaultOrder && !assaultOrder.IsValidOrder(invadingFleet))
                    invadingFleet.SetOrder(invadingFleet.GetDefaultOrder());
            }

            ParallelForEach(GameContext.Current.Universe.Find<Colony>(), c =>
            {
                GameContext.PushThreadContext(game);
                try { c.RefreshShielding(true); }
                finally { GameContext.PopThreadContext(); }
            });
        }
        #endregion

        #region DoPopulation() Method
        void DoPopulation(GameContext game)
        {
            ParallelForEach(GameContext.Current.Civilizations, civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ.CivID];

                    civManager.TotalPopulation.Reset();

                    foreach (Colony colony in civManager.Colonies)
                    {
                        colony.Population.Maximum = colony.MaxPopulation;

                        int popChange = 0;
                        int foodDeficit;

                        colony.FoodReserves.AdjustCurrent(colony.GetProductionOutput(ProductionCategory.Food));
                        foodDeficit = Math.Min(colony.FoodReserves.CurrentValue - colony.Population.CurrentValue, 0);
                        colony.FoodReserves.AdjustCurrent(-1 * colony.Population.CurrentValue);
                        colony.FoodReserves.UpdateAndReset();

                        /*
                         * If there is not enough food to feed the population, we need to kill off some of the
                         * population due to starvation.  Otherwise, we increase the population according to the
                         * growth rate if we did not suffer a loss due to starvation during the previous turn.
                         * We want to ensure that there is a 1-turn period between population loss and recovery.
                         */
                        //if (colony.Name == "Ledos")
                        //    ; // ddd;

                        Percentage growthRate = colony.GrowthRate;


                        if (foodDeficit < 0)
                        {
                            popChange = -(int)Math.Floor(0.1 * Math.Sqrt(Math.Abs(colony.Population.CurrentValue * foodDeficit)));
                            civManager.SitRepEntries.Add(new StarvationSitRepEntry(civ, colony));
                        }
                        else
                        {
                            popChange = (int)Math.Ceiling(growthRate * colony.Population.CurrentValue);
                        }

                        if (growthRate < 0)
                        {
                            civManager.SitRepEntries.Add(new PopulationDyingSitRepEntry(civ, colony));
                        }

                        int newPopulation = colony.Population.AdjustCurrent(popChange);

                        // TODO: We need to figure out how to deal with a civilization having no colonies
                        // and no colony ships
                        if (colony.Population.CurrentValue == 0)
                        {
                            civManager.SitRepEntries.Add(new PopulationDiedSitRepEntry(civ, colony));
                            colony.Destroy();
                            civManager.EnsureSeatOfGovernment();
                            return;
                        }
                        colony.Population.UpdateAndReset();
                        civManager.TotalPopulation.AdjustCurrent(colony.Population.CurrentValue);

                        if (newPopulation < colony.Population.Maximum)
                        {
                            ProductionFacilityDesign foodFacilityType = colony.GetFacilityType(ProductionCategory.Food);
                            if ((foodFacilityType != null) && (colony.GetAvailableLabor() >= foodFacilityType.LaborCost))
                            {
                                int popInThreeTurns = Math.Min(colony.Population.Maximum,
                                    (int)(newPopulation * (1 + colony.GrowthRate) * (1 + colony.GrowthRate) * (1 + colony.GrowthRate)));
                                while (popInThreeTurns > colony.GetProductionOutput(ProductionCategory.Food))
                                {
                                    if (!colony.ActivateFacility(ProductionCategory.Food))
                                        break;
                                }
                            }
                        }

                        while (colony.ActivateFacility(ProductionCategory.Industry))
                            continue;
                    }

                    civManager.EnsureSeatOfGovernment();
                }
                catch (Exception e)
                {
                    GameLog.Core.General.Error(e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });
        }
        #endregion

        #region DoResearch() Method
        private void DoResearch(GameContext game)
        {
            IEnumerable<Ship> scienceShips = game.Universe.Find<Ship>(UniverseObjectType.Ship).Where(s => s.ShipType == ShipType.Science);
            ParallelForEach(scienceShips, scienceShip =>
            {
                GameContext.PushThreadContext(game);
                if (scienceShip.Sector.System == null)
                {
                    return;
                }
                //GameLog.Core.Research.DebugFormat("{0} {1} is conducting research in {2}...",
                //    scienceShip.ObjectID, scienceShip.Name, scienceShip.Sector);

                try
                {
                    CivilizationManager owner = GameContext.Current.CivilizationManagers[scienceShip.Owner];
                    StarType starType = scienceShip.Sector.System.StarType;
                    if (scienceShip.Location == owner.HomeSystem.Location)
                        return;

                    int researchGained = (int)(scienceShip.ShipDesign.ScanStrength * scienceShip.ShipDesign.ScienceAbility);
                    researchGained += 1;

                    // works GameLog.Core.Research.DebugFormat("Turn {3}: Base research gained for {0} {1} is {2}",
                    //scienceShip.ObjectID, scienceShip.Name, researchGained, GameContext.Current.TurnNumber);

                    switch (starType)
                    {
                        case StarType.Nebula:
                            researchGained *= 20;  // multiplied with 5
                            break;
                        case StarType.Blue:
                        case StarType.Orange:
                        case StarType.Red:
                        case StarType.White:
                        case StarType.Yellow:
                            researchGained *= 10;
                            break;

                        case StarType.XRayPulsar:
                        case StarType.RadioPulsar:
                        case StarType.NeutronStar:
                            researchGained *= 15;
                            break;

                        case StarType.BlackHole:
                        case StarType.Quasar:
                            researchGained *= 20;
                            break;

                        case StarType.Wormhole:
                            researchGained *= 30;
                            break;

                        default:
                            researchGained = 1;
                            break;
                    }




                    GameContext.Current.CivilizationManagers[scienceShip.Owner].Research.UpdateResearch(researchGained);


                    //works   GameLog.Core.Research.DebugFormat("{0} {1} gained {2} research points for {3} by studying the {4} in {5}",
                    //    scienceShip.ObjectID, scienceShip.Name, researchGained, owner.Civilization.Key, starType, scienceShip.Sector);

                    GameContext.Current.CivilizationManagers[owner].SitRepEntries.Add(new ScienceShipResearchGainedSitRepEntry(owner.Civilization, scienceShip, researchGained));
                }
                catch (Exception e)
                {
                    // Check TechObj for correct (formatted) values for ScienceAbility and ScanStrength
                    GameLog.Core.Research.ErrorFormat(string.Format("##### There was a problem conducting research for {0} {1}",
                        scienceShip.ObjectID, scienceShip.Name),
                        e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });

            ParallelForEach(GameContext.Current.Civilizations, civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ.CivID];
                    civManager.Research.UpdateResearch(
                        civManager.Colonies.Sum(c => c.GetProductionOutput(ProductionCategory.Research)));
                }
                catch (Exception e)
                {
                    GameLog.Core.General.Error(string.Format("DoResearch failed for {0}", civ.Name), e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });
        }
        #endregion

        #region DoMapUpdates() Method
        private void DoMapUpdates(GameContext game)
        {
            DoSectorClaims(game);

            GameContext.PushThreadContext(game);

            SectorMap map = game.Universe.Map;

            Task<int[,]> interference = new Task<int[,]>(() =>
            {
                int[,] array = new int[map.Width, map.Height];

                GameContext.PushThreadContext(game);
                try
                {
                    foreach (StarSystem starSystem in game.Universe.Find(UniverseObjectType.StarSystem))
                        StarHelper.ApplySensorInterference(array, starSystem);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }

                return array;
            });

            interference.Start();

            ParallelForEach(game.Civilizations, civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    HashSet<MapLocation> fuelLocations = new HashSet<MapLocation>();
                    CivilizationManager civManager = game.CivilizationManagers[civ];
                    CivilizationMapData mapData = civManager.MapData;

                    mapData.ResetScanStrengthAndFuelRange();
                    //fleets
                    foreach (Fleet fleet in game.Universe.FindOwned<Fleet>(civ))
                    {
                        //GameLog.Core.MapData.DebugFormat("UpgradeScanStrength from FLEET {0} {1} ({2}) at {3}, ScanStrength = {4}, Range = {5}", fleet.ObjectID, fleet.Name, 
                        //    fleet.Owner, fleet.Location, fleet.ScanStrength, fleet.SensorRange);
                        mapData.UpgradeScanStrength(
                            fleet.Location,
                            fleet.ScanStrength,
                            fleet.SensorRange,
                            0,
                            1);
                    }
                    /*stations */
                    foreach (Station station in game.Universe.FindOwned<Station>(civ))
                    {
                        //GameLog.Core.MapData.DebugFormat("UpgradeScanStrength from STATION {0} {1} ({2}) at {3}, ScanStrength = {4}, Range = {5}", station.ObjectID, station.Name, 
                        //    station.Owner, station.Location, station.StationDesign.ScanStrength, station.StationDesign.SensorRange);
                        mapData.UpgradeScanStrength(
                            station.Location,
                            station.StationDesign.ScanStrength,
                            station.StationDesign.SensorRange,
                            0,
                            1);

                        fuelLocations.Add(station.Location);
                        /* stations of other civs we can use to travel */
                        foreach (Civilization whoElse in game.Civilizations)
                        {
                            List<Civilization> aggreableCivs = (from Civilization in GameContext.Current.Civilizations
                                                                where GameContext.Current.AgreementMatrix.IsAgreementActive(civ, whoElse, ClauseType.TreatyDefensiveAlliance) ||
                                                                      GameContext.Current.AgreementMatrix.IsAgreementActive(civ, whoElse, ClauseType.TreatyFullAlliance) ||
                                                                      GameContext.Current.AgreementMatrix.IsAgreementActive(civ, whoElse, ClauseType.TreatyAffiliation)
                                                                select whoElse).ToList();
                            if (aggreableCivs != null)
                            {
                                foreach (Civilization who in aggreableCivs)
                                {
                                    foreach (Station anotherSation in game.Universe.FindOwned<Station>(who))
                                    {
                                        if (anotherSation != null)
                                            fuelLocations.Add(anotherSation.Location);
                                    }
                                }
                            }
                        }
                    }

                    foreach (Colony colony in civManager.Colonies)
                    {
                        int scanModifier = 0;

                        IEnumerable<int> scanBonuses = colony.Buildings
                            .Where(o => o.IsActive)
                            .SelectMany(o => o.BuildingDesign.Bonuses)
                            .Where(o => o.BonusType == BonusType.ScanRange)
                            .Select(o => o.Amount);

                        if (scanBonuses.Any())
                            scanModifier = scanBonuses.Max();

                        //GameLog.Core.MapData.DebugFormat("UpgradeScanStrength from COLONY {0} {1} ({2}) at  {3}, ScanStrength = {4}, Range = {5}", colony.ObjectID, colony.Name, 
                        //    colony.Owner, colony.Location, 1 + scanModifier, 1 + scanModifier);  
                        mapData.UpgradeScanStrength(
                            colony.Location,
                            1 + scanModifier,
                            1 + scanModifier,
                            0,
                            1);

                        if (colony.Shipyard != null)
                            fuelLocations.Add(colony.Location);
                    }

                    for (int x = 0; x < map.Width; x++)
                    {
                        for (int y = 0; y < map.Height; y++)
                        {
                            Sector sector = map[x, y];

                            foreach (MapLocation fuelLocation in fuelLocations)
                            {
                                mapData.UpgradeFuelRange(
                                    sector.Location,
                                    MapLocation.GetDistance(fuelLocation, sector.Location));
                            }
                        }
                    }

                    mapData.ApplyScanInterference(interference.Result);
                }
                catch (Exception e)
                {
                    GameLog.Core.General.ErrorFormat(string.Format("DoMapUpdate failed for {0}",
                        civ.Name),
                        e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });
        }
        #endregion

        #region DoSectorClaims() Method
        private void DoSectorClaims(GameContext game)
        {
            SectorMap map = game.Universe.Map;
            SectorClaimGrid sectorClaims = game.SectorClaims;

            sectorClaims.ClearClaims();

            ParallelForEach(GameContext.Current.Civilizations.Where(o => o.IsEmpire).ToList(), civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ];

                    foreach (Colony colony in civManager.Colonies)
                    {
                        int minX = colony.Location.X;
                        int minY = colony.Location.Y;
                        int maxX = colony.Location.X;
                        int maxY = colony.Location.Y;
                        int radius = Math.Min(colony.Population.CurrentValue / 100, 3);

                        minX = Math.Max(0, minX - radius);
                        minY = Math.Max(0, minY - radius);
                        maxX = Math.Min(map.Width - 1, maxX + radius);
                        maxY = Math.Min(map.Height - 1, maxY + radius);

                        for (int x = minX; x <= maxX; x++)
                        {
                            for (int y = minY; y <= maxY; y++)
                            {
                                MapLocation location = new MapLocation(x, y);

                                int claimWeight = colony.Population.CurrentValue / (MapLocation.GetDistance(location, colony.Location) + 1);

                                if (claimWeight <= 0)
                                    continue;

                                lock (sectorClaims)
                                    sectorClaims.AddClaim(location, civ, claimWeight);

                                civManager.MapData.SetScanned(location, true);
                                /* look for ships in violation of Non_Agression (no go into others space) treaty */
                                foreach (Civilization whoElse in GameContext.Current.Civilizations)
                                {
                                    //if (whoElse == civ)
                                    //    continue;
                                    if (GameContext.Current.AgreementMatrix.IsAgreementActive(civ, whoElse, ClauseType.TreatyNonAggression))
                                    {
                                        GameLog.Core.Diplomacy.DebugFormat("*******Looking for NonAggression Treaties*******");
                                        List<Fleet> whosFleets = GameContext.Current.Universe.Find<Fleet>().Where(o => o.Owner == whoElse).ToList();
                                        foreach (Fleet fleet in whosFleets)
                                        {
                                            if (sectorClaims.GetOwner(fleet.Location) == civ)
                                            {
                                                GameLog.Core.Diplomacy.DebugFormat("Got NonAggression Treaty for {0} vs {1}, trying for regard trust change and canel treaties", civ.Key, whoElse.Key);
                                                DiplomacyHelper.ApplyRegardChange(civ, whoElse, -200);
                                                DiplomacyHelper.ApplyTrustChange(civ, whoElse, -200);
                                                //var activeAgreements = GameContext.Current.AgreementMatrix[civ.CivID, whoElse.CivID];
                                                /* cancel all agreements */
                                                //while (activeAgreements.Count > 0)
                                                //{
                                                //    BreakAgreementVisitor.BreakAgreement(activeAgreements[0]);
                                                //}
                                                /* sitrep for canceling all agreements */
                                                //if (civ.IsEmpire)
                                                //{
                                                //    civManager.SitRepEntries.Add(new ViolateTreatySitRepEntry(civ, whoElse));
                                                //    //civManager.SitRepEntries.Add(new ViolateTreatySitRepEntry(whoElse, civ));
                                                //}
                                                ForeignPower foreignPower = new ForeignPower(civ, whoElse);
                                                foreignPower.ViolateNonAggression(whoElse);
                                                ForeignPower otherForeignPower = new ForeignPower(whoElse, civ);
                                                otherForeignPower.ViolateNonAggression(whoElse);
                                            }
                                        }
                                    }
                                    //GameLog.Core.MapData.DebugFormat("{0} (Colony owner: {1}): SetScanned to -> True ", location.ToString(), colony.Owner);
                                }
                            }
                        }
                    }

                    if (civ.IsHuman)
                        civManager.DesiredBorders = new ConvexHullSet(Enumerable.Empty<ConvexHull>());
                    //PlayerAI.CreateDesiredBorders(civ);

                }
                catch (Exception e)
                {
                    GameLog.Core.General.ErrorFormat(string.Format("DoSectorClaims failed for {0}",
                        civ.Name),
                        e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });
        }
        #endregion

        #region DoScrapping() Method
        void DoScrapping(GameContext game)
        {
            GameContext priorThreadContext = GameContext.ThreadContext;
            GameContext.PushThreadContext(game);
            try
            {
                foreach (TechObject scrappedObject in game.Universe.Find<TechObject>().Where(o => o.Scrap))
                    game.Universe.Scrap(scrappedObject);

                IEnumerable<Colony> colonies = game.Civilizations
                    .Select(o => game.CivilizationManagers[o.CivID])
                    .SelectMany(o => o.Colonies);

                foreach (Colony colony in colonies)
                    game.Universe.ScrapNonStructures(colony);
            }
            finally
            {
                GameContext.PopThreadContext();
            }
        }
        #endregion

        #region DoMaintenance() Method
        private void DoMaintenance(GameContext game)
        {
            foreach (Civilization civ in GameContext.Current.Civilizations)
            {
                int _civMaintance = 0;

                CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ];
                foreach (Colony colony in civManager.Colonies)
                {
                    int _energyPF_unused = colony.TotalEnergyFacilities - colony.GetActiveFacilities(ProductionCategory.Energy);
                    GameLog.Core.Energy.DebugFormat(" Turn {0}: {1} Energy Facilities unused at {2} {3} {4} "
                        , GameContext.Current.TurnNumber
                        , _energyPF_unused
                        , colony.Name
                        , colony.Location
                        , colony.Owner
                        );

                    if (colony.NetEnergy < 0 && _energyPF_unused > 0) colony.HandlePF();  // for energy shortage try to increase energy

                    int _shutdowned = colony.EnsureEnergyForBuildings();

                    if (_shutdowned > 0)
                        GameLog.Core.Energy.DebugFormat(" Turn {0}: Energy Shutdown for {1} building at {2} {3} {4} "
                        , GameContext.Current.TurnNumber
                        , _shutdowned
                        , colony.Name
                        , colony.Location
                        , colony.Owner
                        );
                }

                foreach (TechObject item in GameContext.Current.Universe.FindOwned<TechObject>(civ))
                {
                    _civMaintance += item.Design.MaintenanceCost;

                    // works
                    //if (item.Design.MaintenanceCost > 0)
                    //    GameLog.Core.Credits.DebugFormat("Turn {0}: {4} MaintenanceCost for {1} {3} {2} at {5} {6}"
                    //        , GameContext.Current.TurnNumber
                    //        , item.ObjectID
                    //        , item.Name
                    //        , item.Design
                    //    , item.Design.MaintenanceCost
                    //    , item.Location
                    //        , item.Owner
                    //    );
                }

                civManager.Credits.AdjustCurrent(_civMaintance * -1);
                civManager.MaintenanceCostLastTurn = _civMaintance;

                // works, values part of Log of CivsAndRaces
                //GameLog.Core.Credits.DebugFormat("Turn {0}: {3} _civMaintenanceCost for civ {1} {2} "
                //    , GameContext.Current.TurnNumber
                //    , civ.CivID
                //    , civ.Key
                //    , _civMaintance
                //    );

            }
        }
        #endregion

        #region DoProduction() Method
        void DoProduction(GameContext game)
        {
            /*
             * Break down production by civilization.  We want to use resources
             * from both the colonies and the global reserves, so this is the
             * sensible way to do it.
             */
            int _civsToDo = 0; // for Debug
            _civsToDo = GameContext.Current.Civilizations.Count;

            foreach (Civilization civ in GameContext.Current.Civilizations)
            {
                GameContext.PushThreadContext(game);

                //GameLog.Core.Production.DebugFormat("#####################################################");
                //string _gameTurnNumber = GameContext.Current.TurnNumber.ToString();
                GameLog.Core.Production.DebugFormat(Environment.NewLine + "   Turn {0}: ######################################### DoProduction for Civs ({2} to do): > {1}"
                    , GameContext.Current.TurnNumber, civ.Name, _civsToDo);

                _civsToDo -= 1;

                try
                {
                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ.CivID];
                    List<Colony> colonies = new List<Colony>(civManager.Colonies);

                    /*
                     * Update the civilization's treasury and resource stockpile to include anything that was
                     * generated by this colony. *EVERYTHING* must be add to the global pool prior to checking
                     * for negative and possibly blocking production.
                     */
                    int newCredits = colonies.Sum(c => c.TaxCredits);
                    int newIntelligenceDefense = colonies.Sum(c => c.NetIntelligence) * 3 / 10; // 30 % into Defense
                    int newIntelligenceAttacking = colonies.Sum(c => c.NetIntelligence) * 7 / 10; // 70 % into AttackingAccumulation
                    int newDeuterium = colonies.Sum(c => c.NetDeuterium);
                    int newDilithium = colonies.Sum(c => c.NetDilithium);
                    int newRawMaterials = colonies.Sum(c => c.NetRawMaterials);

                    civManager.Credits.AdjustCurrent(newCredits);
                    civManager.TotalIntelligenceDefenseAccumulated.AdjustCurrent(newIntelligenceDefense);
                    civManager.TotalIntelligenceAttackingAccumulated.AdjustCurrent(newIntelligenceAttacking);
                    civManager.Resources.Deuterium.AdjustCurrent(newDeuterium);
                    civManager.Resources.Dilithium.AdjustCurrent(newDilithium);
                    civManager.Resources.RawMaterials.AdjustCurrent(newRawMaterials);

                    GameLog.Core.Production.DebugFormat("Turn {5}: {0} credits, {1} deuterium, {2} dilithium, {3} duranium added from all colonies to {4} ",
                        newCredits, newDeuterium, newDilithium, newRawMaterials, civManager.Civilization, GameContext.Current.TurnNumber);
                    GameLog.Client.Production.DebugFormat("Turn {3}: Civ Manager = {0} TotalIntelDefenseAccumulated = {1}, TotalIntelAccumulated = {2}",
                        civManager.Civilization.Key,
                        civManager.TotalIntelligenceDefenseAccumulated.CurrentValue,
                        civManager.TotalIntelligenceAttackingAccumulated.CurrentValue
                        , GameContext.Current.TurnNumber
                        );
                    //Get the resources available for the civilization
                    ResourceValueCollection totalResourcesAvailable = new ResourceValueCollection
                    {
                        [ResourceType.Deuterium] = civManager.Resources.Deuterium.CurrentValue,
                        [ResourceType.Dilithium] = civManager.Resources.Dilithium.CurrentValue,
                        [ResourceType.RawMaterials] = civManager.Resources.RawMaterials.CurrentValue
                    };

                    GameLog.Core.Production.DebugFormat("Turn {5}: {0} credits, {1} deuterium, {2} dilithium, {3} duranium available in total for {4}"
                        , civManager.Credits.CurrentValue
                        , civManager.Resources.Deuterium.CurrentValue
                        , civManager.Resources.Dilithium.CurrentValue
                        , civManager.Resources.RawMaterials.CurrentValue
                        , civManager.Civilization
                        , GameContext.Current.TurnNumber
                        );

                    /* 
                     * Shuffle the colonies so they are processed in random order.  This
                     * will help prevent the same colonies from getting priority when
                     * the global stockpiles are low.
                     */
                    colonies.RandomizeInPlace();

                    int _coloniesToDo = 0;
                    _coloniesToDo = colonies.Count;

                    /* Iterate through each colony */
                    foreach (Colony colony in colonies)
                    {
                        colony.HandlePF();

                        GameLog.Core.Production.DebugFormat("--------------------------------------------------------------");
                        GameLog.Core.Production.DebugFormat("Turn {0}: {1} undone colonies = {5}, Credits = {2}, Health = {4} - DoProduction for Colony {3}"
                            , GameContext.Current.TurnNumber
                            , civ.Key
                            , civManager.Credits
                            , colony.Name
                            , colony.Health
                            , _coloniesToDo
                            ); // last = 4

                        _coloniesToDo -= -1;

                        string prevProject = "";

                        //See if there is actually anything to build for this colony
                        if (!colony.BuildSlots[0].HasProject && colony.BuildQueue.IsEmpty())
                        {
                            GameLog.Core.Production.DebugFormat("Turn {2}: Nothing to do for Colony {0} ({1})"
                                , colony.Name, civ.Name, GameContext.Current.TurnNumber);
                            civManager.SitRepEntries.Add(new BuildQueueEmptySitRepEntry(civ, colony, false));
                            continue;
                        }

                        if (!colony.IsProductionAutomated)
                            colony.ClearBuildPrioritiesAndConsolidate();

                        /* We want to capture the industry output available at the colony. */
                        int industry = colony.NetIndustry;

                        string currProject = "";

                        int _colonyBuildProject_SameTurn = 0;

                        if (colony.BuildQueue.IsEmpty())
                            civManager.SitRepEntries.Add(new BuildQueueEmptySitRepEntry(civ, colony, false));

                        //Start going through the queue
                        while ((industry > 0) && ((!colony.BuildQueue.IsEmpty()) || colony.BuildSlots[0].HasProject))
                        {
                            _colonyBuildProject_SameTurn += 1;
                            //Move the top of the queue in to the build slot
                            if (!colony.BuildSlots[0].HasProject)
                            {
                                colony.ProcessQueue();
                            }

                            currProject = colony.Name + ": " + colony.BuildSlots[0].Project.BuildDesign.Name;
                            if (currProject == prevProject)
                                /*//breakpoint*/
                                GameLog.Core.Production.DebugFormat("currProject == prevProject")
                                    ;


                            //Check to see if the colony has reached the limit for this building
                            if (TechTreeHelper.IsBuildLimitReached(colony, colony.BuildSlots[0].Project.BuildDesign))
                            {
                                GameLog.Core.Production.WarnFormat("Removing {0} from queue on {1} ({2}) - Build Limit Reached", colony.BuildSlots[0].Project.BuildDesign.Name, colony.Name, civ.Name);
                                colony.BuildSlots[0].Project.Cancel();
                                break;
                            }

                            if (colony.BuildSlots[0].Project.IsPaused) { }
                            //TODO: Not sure how to handle this

                            GameLog.Core.Production.DebugFormat(Environment.NewLine + "       Turn {8}: Income TradeRoute={4}, Tax={3}, Deuterium={5}, Dilithium={6}, RawMaterials={7} available for {0} before construction of {1} on {2}" + Environment.NewLine,
                                civ.Name,
                                colony.BuildSlots[0].Project.BuildDesign.Name,
                                colony.Name,
                                colony.TaxCredits,
                                colony.CreditsFromTrade,
                                totalResourcesAvailable[ResourceType.Deuterium],
                                totalResourcesAvailable[ResourceType.Dilithium],
                                totalResourcesAvailable[ResourceType.RawMaterials]
                                , GameContext.Current.TurnNumber
                                );

                            //if (colony.BuildSlots[0].Project.BuildDesign.Name == "SUBSPACE_JAMMER" && colony.OwnerID == 0)
                            //    /*Breakpoint*/
                            //    ;
                            //Try to finish the projects
                            if (colony.BuildSlots[0].Project.IsRushed)
                            {
                                // Rushing a project should have no impact on the industry of colony (since it's all been paid for)
                                int tmpIndustry = colony.BuildSlots[0].Project.GetCurrentIndustryCost();
                                ResourceValueCollection tmpResources = new ResourceValueCollection
                                {
                                    [ResourceType.Deuterium] = 999999,
                                    [ResourceType.Dilithium] = 999999,
                                    [ResourceType.RawMaterials] = 999999
                                };
                                civManager.Credits.AdjustCurrent(colony.BuildSlots[0].Project.GetTotalCreditsCost());
                                colony.BuildSlots[0].Project.Advance(ref tmpIndustry, tmpResources);
                                GameLog.Core.ProductionDetails.DebugFormat("Turn {4}: BUY: {0} credits applied to {1} on {2} ({3})",
                                    tmpIndustry,
                                    colony.BuildSlots[0].Project.BuildDesign.Name,
                                    colony.Name,
                                    civ.Name,
                                    GameContext.Current.TurnNumber
                                    );
                            }
                            else
                            {
                                ResourceValueCollection totalResourcesBefore = totalResourcesAvailable.Clone();

                                //cheat (necessary for never ending build projects)
                                if (industry < 10)
                                    industry = 10;

                                colony.BuildSlots[0].Project.Advance(ref industry, totalResourcesAvailable);

                                int _deuteriumUsed = totalResourcesBefore[ResourceType.Deuterium];
                                int _deuteriumavailable = totalResourcesAvailable[ResourceType.Deuterium];

                                //Figure out how what resources have been used
                                int deuteriumUsed = totalResourcesBefore[ResourceType.Deuterium] - totalResourcesAvailable[ResourceType.Deuterium];
                                int dilithiumUsed = totalResourcesBefore[ResourceType.Dilithium] - totalResourcesAvailable[ResourceType.Dilithium];
                                int rawMaterialsUsed = totalResourcesBefore[ResourceType.RawMaterials] - totalResourcesAvailable[ResourceType.RawMaterials];

                                GameLog.Core.ProductionDetails.DebugFormat(Environment.NewLine + "   Turn {5}: passing={6}, {7} industry, {0} deuterium, {1} dilithium, {2} duranium applied to project {3} on {4} {9}, {8} percent done" + Environment.NewLine
                                    , deuteriumUsed, dilithiumUsed, rawMaterialsUsed
                                    , colony.BuildSlots[0].Project, colony, GameContext.Current.TurnNumber
                                    , _colonyBuildProject_SameTurn, industry, colony.BuildSlots[0].Project.PercentComplete
                                    , colony.Location
                                    );

                                civManager.Resources.Deuterium.AdjustCurrent(-1 * deuteriumUsed);
                                civManager.Resources.Dilithium.AdjustCurrent(-1 * dilithiumUsed);
                                civManager.Resources.RawMaterials.AdjustCurrent(-1 * rawMaterialsUsed);

                                if (_colonyBuildProject_SameTurn > 4)
                                {
                                    GameLog.Core.ProductionDetails.DebugFormat(Environment.NewLine + "   Turn {3}: Construction of {0} forced to be finished on {1} ({2})" + Environment.NewLine
                                       , colony.BuildSlots[0].Project.BuildDesign.Name, colony.Name, civ.Name, GameContext.Current.TurnNumber);
                                    colony.BuildSlots[0].Project.Finish();
                                    colony.BuildSlots[0].Project = null;
                                    _colonyBuildProject_SameTurn = 0;
                                    continue;
                                }
                            }

                            if (colony.BuildSlots[0].Project.IsCompleted)
                            {
                                GameLog.Core.Production.DebugFormat(Environment.NewLine + "   Turn {3}: ############### FINISHED: Construction of {0} finished on {1} ({2})" + Environment.NewLine
                                    , colony.BuildSlots[0].Project.BuildDesign.Name, colony.Name, civ.Name, GameContext.Current.TurnNumber);
                                colony.BuildSlots[0].Project.Finish();
                                colony.BuildSlots[0].Project = null;
                                continue;
                            }
                            //GameLog.Core.Production.DebugFormat(string.Format("Turn {0}: DoProduction DONE for {1} ({2})" + Environment.NewLine + "-----",
                            //    GameContext.Current.TurnNumber, colony.Name, civ.Name));
                            //// continue as well if not finish
                            //break;
                        }

                        if (!colony.BuildSlots[0].HasProject && colony.BuildQueue.IsEmpty())
                        {
                            civManager.SitRepEntries.Add(new BuildQueueEmptySitRepEntry(civ, colony, false));
                        }
                        else
                            //go on 
                            GameLog.Core.ProductionDetails.DebugFormat(string.Format("Turn {0}: DoProduction - BuildQueue*s* not empty for {1} ({2})" + Environment.NewLine + "-----",
                            GameContext.Current.TurnNumber, colony.Name, civ.Name));
                        // above SitRep added if colony is finished and empty

                        GameLog.Core.ProductionDetails.DebugFormat(string.Format("Turn {0}: DoProduction DONE for {1} ({2})" + Environment.NewLine + "-----",
                            GameContext.Current.TurnNumber, colony.Name, civ.Name));
                        // continue as well if not finish
                        break;
                    }// end for each civ
                }
                catch (Exception e)
                {
                    GameLog.Core.Production.Error(string.Format("DoProduction failed for {0}", civ.Name), e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            }

        }
        #endregion

        #region DoShipProduction() Method
        private void DoShipProduction(GameContext game)
        {
            /*
             * Break down production by civilization.  We want to use resources
             * from both the colonies and the global reserves, so this is the
             * sensible way to do it.
             */
            ParallelForEach(GameContext.Current.Civilizations, civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ.CivID];
                    //Get all colonies with a shipyard
                    List<Colony> colonies = civManager.Colonies.Where(c => c.Shipyard != null).ToList();
                    /* 
                     * Shuffle the colonies so they are processed in random order. This
                     * will help prevent the same colonies from getting priority when
                     * the global stockpiles are low.
                     */
                    colonies.RandomizeInPlace();

                    //Get the resources available for the civilization
                    ResourceValueCollection totalResourcesAvailable = new ResourceValueCollection
                    {
                        [ResourceType.Deuterium] = civManager.Resources.Deuterium.CurrentValue,
                        [ResourceType.Dilithium] = civManager.Resources.Dilithium.CurrentValue,
                        [ResourceType.RawMaterials] = civManager.Resources.RawMaterials.CurrentValue
                    };

                    foreach (Colony colony in colonies)
                    {
                        Shipyard shipyard = colony.Shipyard;
                        IList<BuildQueueItem> queue = shipyard.BuildQueue;

                        //int colonyHealth = Int32.TryParse(colony.Health.ToString(), out int _health);

                        if (!colony.Population.IsMaximized && colony.GrowthRate == 0) // && Int32.TryParse(colony.Health.ToString(), out int _health) != 100)
                        {
                            civManager.SitRepEntries.Add(new GrowthByHealthSitRepEntry(civ, colony));
                        }


                        List<ShipyardBuildSlot> buildSlots = colony.Shipyard.BuildSlots.Where(s => s.IsActive && !s.OnHold).ToList();
                        foreach (ShipyardBuildSlot slot in buildSlots)
                        {

                            // GameLog is making trouble
                            /*GameLog.Core.ShipProduction.DebugFormat("Resources available for {0} before construction of {1} on {2}: Deuterium={3}, Dilithium={4}, RawMaterials={5}",
                                civ.Name,
                                slot.Project.BuildDesign.Name,
                                colony.Name,
                                totalResourcesAvailable[ResourceType.Deuterium],
                                totalResourcesAvailable[ResourceType.Dilithium],
                                totalResourcesAvailable[ResourceType.RawMaterials]);
                             */

                            int output = shipyard.GetBuildOutput(slot.SlotID);
                            while ((slot.HasProject || !shipyard.BuildQueue.IsEmpty()) && (output > 0))
                            {
                                if (!slot.HasProject)
                                {
                                    //slot.ProcessQueue();
                                    shipyard.ProcessQueue();
                                }
                                if (!slot.HasProject && shipyard.BuildQueue.IsEmpty())
                                {
                                    GameLog.Core.ShipProduction.DebugFormat("Nothing to do for Shipyard Slot {0} on {1} ({2})",
                                        slot.SlotID,
                                        colony.Name,
                                        civ.Name);
                                    continue;
                                }

                                ResourceValueCollection totalResourcesBefore = totalResourcesAvailable.Clone();
                                slot.Project.Advance(ref output, totalResourcesAvailable);

                                //Figure out how what resources have been used
                                int deuteriumUsed = totalResourcesBefore[ResourceType.Deuterium] - totalResourcesAvailable[ResourceType.Deuterium];
                                int dilithiumUsed = totalResourcesBefore[ResourceType.Dilithium] - totalResourcesAvailable[ResourceType.Dilithium];
                                int rawMaterialsUsed = totalResourcesBefore[ResourceType.RawMaterials] - totalResourcesAvailable[ResourceType.RawMaterials];
                                civManager.Resources.Deuterium.AdjustCurrent(-1 * deuteriumUsed);
                                civManager.Resources.Dilithium.AdjustCurrent(-1 * dilithiumUsed);
                                civManager.Resources.RawMaterials.AdjustCurrent(-1 * rawMaterialsUsed);

                                GameLog.Core.Production.DebugFormat(Environment.NewLine + "       Turn {5}: on {4} ({6}):{0} deuterium, {1} dilithium, {2} duranium applied to {3} " + Environment.NewLine,
                                    deuteriumUsed, dilithiumUsed, rawMaterialsUsed, slot.Project, colony, GameContext.Current.TurnNumber, colony.Owner);

                                if (slot.Project.IsCompleted)
                                {
                                    GameLog.Core.ShipProduction.DebugFormat("Turn {4}: {0} in Shipyard Slot {1} on {2} ({3}) is finished",
                                        slot.Project.BuildDesign,
                                        slot.SlotID,
                                        colony.Name,
                                        civ.Name
                                        , GameContext.Current.TurnNumber
                                        );
                                    slot.Project.Finish();
                                    slot.Project = null;
                                }
                                else
                                {
                                    //if there is a gap for raw materials than code would go into never ending loop without the break
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    GameLog.Core.ShipProduction.Error(string.Format("DoShipProduction failed for {0}", civ.Name), e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });
        }
        #endregion

        #region DoMorale() Method
        void DoMorale(GameContext game)
        {
            ConcurrentStack<Exception> errors = new System.Collections.Concurrent.ConcurrentStack<Exception>();

            ParallelForEach(GameContext.Current.Civilizations, civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    int globalMorale = 0;
                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ.CivID];

                    /* Calculate any empire-wide morale bonuses */
                    foreach (Bonus bonus in civManager.GlobalBonuses)
                    {
                        if (bonus.BonusType == BonusType.MoraleEmpireWide)
                            globalMorale += bonus.Amount;
                    }

                    /* Iterate through each colony. */
                    foreach (Colony colony in civManager.Colonies)
                    {
                        /* Add the empire-wide morale adjustments. */
                        colony.Morale.AdjustCurrent(globalMorale);

                        /* Add any morale bonuses from active buildings at the colony. */
                        int colonyBonus = (from building in colony.Buildings
                                           where building.IsActive
                                           from bonus in building.BuildingDesign.Bonuses
                                           where bonus.BonusType == BonusType.Morale
                                           select bonus.Amount).Sum();

                        colony.Morale.AdjustCurrent(colonyBonus);

                        /*
                         * If morale has not changed in this colony for any reason, then we will
                         * cause the morale level to drift towards the founding civilization's
                         * base morale level.
                         */
                        if (colony.Morale.CurrentChange == 0)
                        {
                            int drift = 0;
                            Civilization originalCiv = colony.OriginalOwner;

                            //We're below the base, so drift up to it
                            if (colony.Morale.CurrentValue < originalCiv.BaseMoraleLevel)
                            {
                                drift = originalCiv.MoraleDriftRate;
                            }
                            //We're above the base, so drift down to it
                            else if (colony.Morale.CurrentValue > originalCiv.BaseMoraleLevel)
                            {
                                drift = -originalCiv.MoraleDriftRate;
                            }

                            colony.Morale.AdjustCurrent(drift);
                        }

                        colony.Morale.UpdateAndReset();
                    }
                }
                catch (Exception e)
                {
                    errors.Push(e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }
            });

            if (!errors.IsEmpty)
                throw new AggregateException(errors);
        }
        #endregion

        #region DoTrade() Method
        void DoTrade(GameContext game)
        {
            Table popReqTable = GameContext.Current.Tables.ResourceTables["TradeRoutePopReq"];
            Table popModTable = GameContext.Current.Tables.ResourceTables["TradeRoutePopMultipliers"];

            float sourceMod = Number.ParseSingle(popModTable["Source"][0]);
            float targetMod = Number.ParseSingle(popModTable["Target"][0]);

            ParallelForEach(GameContext.Current.Civilizations, civ =>
            {
                GameContext.PushThreadContext(game);
                try
                {
                    int popForTradeRoute;
                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ.CivID];

                    /*
                     * See what the minimum population level is for a new trade route for the
                     * current civilization.  If one is not specified, use the default.
                     */
                    if (popReqTable[civManager.Civilization.Key] != null)
                        popForTradeRoute = Number.ParseInt32(popReqTable[civManager.Civilization.Key][0]);
                    else
                        popForTradeRoute = Number.ParseInt32(popReqTable[0][0]);

                    HashSet<Colony> colonies = GameContext.Current.Universe.FindOwned<Colony>(civ);

                    /* Iterate through each colony... */
                    foreach (Colony colony in colonies)
                    {
                        /*
                         * For each established trade route, ensure that the target colony is
                         * a valid choice.  If it isn't, break it.  Otherwise, calculate the
                         * revised credit total.
                         */
                        foreach (TradeRoute route in colony.TradeRoutes)
                        {
                            if (!route.IsValidTargetColony(route.TargetColony))
                                route.TargetColony = null;
                            /* do not appear to need this as treaties are already checked some place else? */
                            //if (route.TargetColony != null && route.TargetColony.Owner != null)
                            //{
                            //    var targetCiv = route.TargetColony.Owner;
                            //    if (!GameContext.Current.AgreementMatrix.IsAgreementActive(civ, targetCiv, ClauseType.TreatyDefensiveAlliance) &&
                            //    !GameContext.Current.AgreementMatrix.IsAgreementActive(civ, targetCiv, ClauseType.TreatyFullAlliance) &&
                            //    !GameContext.Current.AgreementMatrix.IsAgreementActive(civ, targetCiv, ClauseType.TreatyAffiliation) &&
                            //    !GameContext.Current.AgreementMatrix.IsAgreementActive(civ, targetCiv, ClauseType.TreatyOpenBorders))
                            //    {
                            //        GameLog.Core.Diplomacy.DebugFormat("!!! NO TRADE ROUTE because no treaty {0} vs {1}", civ, targetCiv);
                            //        route.TargetColony = null;
                            //    }
                            //}
                            if (route.TargetColony != null)
                            {
                                int sourceIndustry = route.SourceColony.NetIndustry + 1;  // avoiding a zero
                                int targetIndustry = route.TargetColony.NetIndustry + 1;

                                route.Credits = 10 * (int)((sourceMod * sourceIndustry) + (targetMod * targetIndustry));

                            }
                        }

                        /*
                         * Calculate how many trade routes the colony is allowed to have.
                         * Take into consideration any routes added by building bonuses.
                         */
                        int tradeRoutes = colony.Population.CurrentValue / popForTradeRoute;

                        tradeRoutes += colony.Buildings
                            .Where(o => o.IsActive)
                            .SelectMany(o => o.BuildingDesign.Bonuses)
                            .Where(o => o.BonusType == BonusType.TradeRoutes)
                            .Sum(o => o.Amount);

                        /*
                         * If the colony doesn't have as many trade routes as it should, then
                         * we need to add some more.
                         */
                        if (tradeRoutes > colony.TradeRoutes.Count)
                        {
                            int tradeRouteDeficit = tradeRoutes - colony.TradeRoutes.Count;
                            for (int i = 0; i < tradeRouteDeficit; i++)
                                colony.TradeRoutes.Add(new TradeRoute(colony));
                        }

                        /*
                         * If the colony has too many trade routes, we need to remove some.
                         * To be generous, we sort them in order of credits generated so that
                         * we remove the least valuable routes.
                         */
                        else if (tradeRoutes < colony.TradeRoutes.Count)
                        {
                            TradeRoute[] extraTradeRoutes = colony.TradeRoutes
                                .OrderByDescending(o => o.Credits)
                                .SkipWhile((o, i) => i < tradeRoutes)
                                .ToArray();
                            foreach (TradeRoute extraTradeRoute in extraTradeRoutes)
                                colony.TradeRoutes.Remove(extraTradeRoute);
                        }

                        /*
                         * Iterate through the remaining trade routes and deposit the credit
                         * income into the civilization's treasury.
                         */
                        foreach (TradeRoute route in colony.TradeRoutes)
                        {
                            colony.CreditsFromTrade.AdjustCurrent(route.Credits);
                            //GameLog.Core.TradeRoutes.DebugFormat("trade route {0}, route is assigned ={1}", route.SourceColony.Owner, route.IsAssigned);
                            if (!route.IsAssigned) // && civManager.SitRepEntries.Any(s=>s.Categories.ToString() == "SpecialEvent"))
                            {
                                //works   GameLog.Core.TradeRoutes.DebugFormat("trade route for {0}, credit {1}=0 should add sitRep", route.SourceColony.Owner, route.SourceColony.CreditsFromTrade.BaseValue);
                                civManager.SitRepEntries.Add(new UnassignedTradeRoute(route));
                            }
                        }
                        /*
                         * Apply all "+% Trade Income" and "+% Credits" bonuses at this colony.
                         */
                        int tradeBonuses = (int)colony.ActiveBuildings
                            .SelectMany(o => o.BuildingDesign.Bonuses)
                            .Where(o => ((o.BonusType == BonusType.PercentTradeIncome) || (o.BonusType == BonusType.PercentCredits)))
                            .Sum(o => 0.01f * o.Amount);

                        colony.CreditsFromTrade.AdjustCurrent(tradeBonuses);
                        civManager.Credits.AdjustCurrent(colony.CreditsFromTrade.CurrentValue);
                        colony.ResetCreditsFromTrade();
                    }

                    /* 
                     * Apply all global "+% Total Credits" bonuses for the civilization.  At present, we have now
                     * completed all adjustments to the civilization's treasury for this turn.  If that changes in
                     * the future, we may need to move this operation.
                     */
                    int globalBonusAdjustment = (int)(0.01f * civManager.GlobalBonuses
                        .Where(o => o.BonusType == BonusType.PercentTotalCredits)
                        .Sum(o => o.Amount));
                    civManager.Credits.AdjustCurrent(globalBonusAdjustment);

                    //theCatch:;
                }
                catch (Exception e)
                {

                    GameLog.Core.Production.DebugFormat(string.Format("DoTrade failed for {0}",
                        civ.Name),
                        e);
                }
                finally
                {
                    GameContext.PopThreadContext();
                }

            });
        }
        #endregion

        #region DoPostTurnOperations() Method
        private void DoPostTurnOperations(GameContext game)
        {
            //DiplomacyHelper.ClearAcceptRejectDictionary(); do this for older turns?
            //IntelHelper.ExecuteIntelOrders(); // now update results of spy operations on host computer, steal and sabotage, remove production facilities, just before we end the turn

            //GameContext.Current.CivilizationManagers[attackedCiv].Credits.AdjustCurrent(stolenCredits * -1);
            //GameContext.Current.CivilizationManagers[attackedCiv].Credits.UpdateAndReset();
            HashSet<Orbital> destroyedOrbitals = GameContext.Current.Universe.Find<Orbital>(o => o.HullStrength.IsMinimized);
            HashSet<Fleet> allFleets = GameContext.Current.Universe.Find<Fleet>(UniverseObjectType.Fleet);

            foreach (Orbital orbital in destroyedOrbitals)
            {
                GameContext.Current.CivilizationManagers[orbital.OwnerID].SitRepEntries.Add(
                    new OrbitalDestroyedSitRepEntry(orbital));
                GameContext.Current.Universe.Destroy(orbital);
            }

            foreach (Fleet fleet in allFleets)
            {
                if (fleet.Ships.Count == 0)
                {
                    if (fleet.Order != null)
                        fleet.Order.OnOrderCancelled();
                    GameContext.Current.Universe.Destroy(fleet);

                }
                else if (fleet.Order != null)
                {
                    fleet.Order.OnTurnEnding();
                }
            }



            foreach (CivilizationManager civManager in GameContext.Current.CivilizationManagers)
            {

                IEnumerable<Ship> allCivShips = GameContext.Current.Universe.Find<Ship>(UniverseObjectType.Ship).Where(o => o.OwnerID == civManager.CivilizationID);

                string civValueShipSummary = /*"(" + civManager.CivilizationID + "> */"LT-ShipSum1: "; //All;" + allCivShips.Count();
                string civValueShipSummary2 = /*"(" + civManager.CivilizationID + "> */"LT-ShipSum2: "; //All;" + allCivShips.Count();  // more civil ships
                int _count = 0;
                int _fp = 0;
                int _fpAll = 0;


                IEnumerable<Ship> commandShips = allCivShips.Where(o => o.ShipType == ShipType.Command);
                _count = commandShips.Count();
                if (_count > 0)
                {
                    _fp = commandShips.LastOrDefault().FirePower.CurrentValue * _count;
                    civValueShipSummary += "Com " + _count + "x (FP:" + _fp + "); "; _fpAll += _fp; // if _count = 0 don't show, there are nothing 
                }


                IEnumerable<Ship> cruiserShips = allCivShips.Where(o => o.ShipType == ShipType.Cruiser || o.ShipType == ShipType.HeavyCruiser || o.ShipType == ShipType.StrikeCruiser);
                //civValueShipSummary += ";Cru;" + _count = cruiserShips.Count();
                _count = cruiserShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = cruiserShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                civValueShipSummary += "Cruiser " + _count + "x (FP:" + _fp + ")"; _fpAll += _fp; // if _count = 0 >>> show 0 


                IEnumerable<Ship> fastAttackShips = allCivShips.Where(o => o.ShipType == ShipType.FastAttack);
                //civValueShipSummary += ";Att;" + _count = fastAttackShips.Count();
                _count = fastAttackShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = fastAttackShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                civValueShipSummary += "; Attack " + _count + "x (FP:" + _fp + ")"; _fpAll += _fp;

                IEnumerable<Ship> scoutShips = allCivShips.Where(o => o.ShipType == ShipType.Scout);
                //civValueShipSummary += ";Sco;" + _count = scoutShips.Count();
                _count = scoutShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = scoutShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                civValueShipSummary += "; Scouts " + _count + "x (FP:" + _fp + ")"; _fpAll += _fp;

                IEnumerable<Ship> scienceShips = allCivShips.Where(o => o.ShipType == ShipType.Science);
                //civValueShipSummary += ";Sci;" + _count = scienceShips.Count();
                _count = scienceShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = scienceShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                // civValueShipSummary2 ... frist line, no semi colon 
                if (_count > 0) civValueShipSummary2 += "Science " + _count + "x (FP:" + _fp + ")"; _fpAll += _fp;

                IEnumerable<Ship> spyShips = allCivShips.Where(o => o.ShipType == ShipType.Spy);
                //civValueShipSummary += ";Spy;" + _count = spyShips.Count();
                _count = spyShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = spyShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                civValueShipSummary2 += "; Spy " + _count + "x ";// (FP:" + _fp + ")";

                IEnumerable<Ship> diplomaticShips = allCivShips.Where(o => o.ShipType == ShipType.Diplomatic);
                //civValueShipSummary += ";Dip;" + _count = diplomaticShips.Count();
                _count = diplomaticShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = diplomaticShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                civValueShipSummary2 += "; Diplo " + _count + "x ";// (FP: " + _fp + ")";

                IEnumerable<Ship> medicalShips = allCivShips.Where(o => o.ShipType == ShipType.Medical);
                //civValueShipSummary += ";Med;" + _count = medicalShips.Count();
                _count = medicalShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = medicalShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                civValueShipSummary2 += "; Medical " + _count + "x ";// (FP: " + _fp + ")";

                IEnumerable<Ship> transportShips = allCivShips.Where(o => o.ShipType == ShipType.Transport);
                //civValueShipSummary += ";Tra;" + _count = transportShips.Count();
                _count = transportShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = transportShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                if (_count > 0) civValueShipSummary2 += "; Transport " + _count + "x ";// (FP: " + _fp + ")";

                IEnumerable<Ship> constructionShips = allCivShips.Where(o => o.ShipType == ShipType.Construction);
                //civValueShipSummary += ";Con;" + _count = constructionShips.Count();
                _count = constructionShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = constructionShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                if (_count > 0) civValueShipSummary2 += "; Construction " + _count + "x ";// (FP: " + _fp + ")"; 

                IEnumerable<Ship> colonyShips = allCivShips.Where(o => o.ShipType == ShipType.Colony);
                //civValueShipSummary += ";Col;" + _count = colonyShips.Count();
                _count = colonyShips.Count();
                _fp = 0;
                if (_count > 0)
                {
                    _fp = colonyShips.LastOrDefault().FirePower.CurrentValue * _count;
                }
                if (_count > 0) civValueShipSummary2 += "; Colony " + _count + "x ";// (FP: " + _fp + ")";

                civValueShipSummary += " - Ships: " + allCivShips.Count() + " - Fire Power Total: " + _fpAll;



                civManager.SitRepEntries.Add(new ShipSummarySitRepEntry(civManager.Civilization, civValueShipSummary));
                civManager.SitRepEntries.Add(new ShipSummarySitRepEntry(civManager.Civilization, civValueShipSummary2));
            }

            foreach (CivValue civ in CivValueList)
            {
                string _text = newline + "   CivValueList: " /*+ civ.AA_CIV_ID*/;
                //_text += civ.CIV_KEY;
                _text += ";Pop;" + civ.CIV_TOT_POP;
                _text += ";Int;" + civ.CIV_TOT_INT;
                _text += ";ID;" + civ.AA_CIV_ID;
                _text += ";" + civ.CIV_KEY;
                //_text += newline + "   " + civValueShipSummary;
                //_text += newline + "   " + civValueShipSummary2;
                GameLog.Core.CivsAndRaces.DebugFormat(_text);
                Console.WriteLine(_text);
            }

            HashSet<Station> allStations = GameContext.Current.Universe.Find<Station>(UniverseObjectType.Station);
            foreach (Station station in allStations)
            {
                string _rep = "Station " + station.ObjectID + " at ";
                _rep += station.Location + ": " + blank + station.ObjectID + blank + station.Name + " (Maint.=" + station.Design.MaintenanceCost + ")";

                CivilizationManager civManager = GameContext.Current.CivilizationManagers[station.OwnerID];
                civManager.SitRepEntries.Add(new ShipStatusSitRepEntry(civManager.Civilization, station.Location, _rep));
            }

            foreach (Fleet fleet in allFleets)
            {
                foreach (Ship ship in fleet.Ships)
                {
                    //if (!fleet.Route.IsEmpty) 
                    string _rep = "Ship ";


                    _rep += ship.ObjectID + " at " + ship.Location + ": " /*+ " < " */+ ship.DesignName + " (Maint.=" + ship.Design.MaintenanceCost + ") >" + blank + ship.Name + " > ";
                    _rep += blank + fleet.Order;
                    if (!fleet.Route.IsEmpty)
                    {
                        MapLocation _aim = fleet.Route.Waypoints.LastOrDefault();
                        Sector _aimSector = GameContext.Current.Universe.Map[_aim];
                        //GameContext.Current.Universe.Find<MapLocation>().TryFindFirstItem(o => o == _aim, out Sector _aimSector);
                        _rep += " # on the way to " + _aim.ToString() + " named " + _aimSector.Name;
                    }

                    CivilizationManager civManager = GameContext.Current.CivilizationManagers[ship.OwnerID];
                    CivilizationManager PlayerCivManager = GameContext.Current.CivilizationManagers[0];  // Federation - can be changed

                    // only own civilization
                    civManager.SitRepEntries.Add(new ShipStatusSitRepEntry(civManager.Civilization, ship.Location, _rep));

                    // all ships shown
                    //PlayerCivManager.SitRepEntries.Add(new ShipStatusSitRepEntry(PlayerCivManager.Civilization, ship.Location, _rep));
                } // end of each ship
            }



            //var allCivs = GameContext.Current.Universe.Find<Fleet>(UniverseObjectType.Fleet);
            foreach (CivilizationManager civManager in GameContext.Current.CivilizationManagers)
            {
                /*
                 * Reset the resource stockpile meters now that we have finished
                 * production for the each civilization.  This will update the
                 * last base value of the meters so that the net change this turn
                 * is properly reflected.  Do the same for the credit treasury.
                 */
                civManager.Resources.UpdateAndReset();
                civManager.Credits.UpdateAndReset();
                //var _countIntelOrders = civManager.IntelOrdersGoingToHost.Count;
                //if (_countIntelOrders > 0)
                //    GameLog.Core.Intel.DebugFormat("{0}: _countIntelOrders = {1}", civManager.Civilization.Key, _countIntelOrders);
                civManager.OnTurnFinished();

                //string civID_and_Turn_Text = civManager.civID_and_Turn); 

                Int32.TryParse(civManager.TotalPopulation.ToString(), out int _totalPopulation);
                Int32.TryParse(civManager.TotalResearch.ToString(), out int _totalResearch);

                AddCivValue(
                    civManager.CivilizationID
                    , civManager.Civilization.Key
                    , civManager.TotalPopulation
                    , civManager.TotalIntelligenceProduction
                    );

                civManager.AddCivHist(civManager.CivilizationID
                    , civManager.Civilization.Key
                    , civManager.Credits.CurrentValue
                    , civManager.Colonies.Count
                    , _totalPopulation
                    , civManager.MaintenanceCostLastTurn
                    , _totalResearch
                    );
                // works - just for DEBUG  // optimized for CSV-Export (CopyPaste)
                GameLog.Core.CivsAndRaces.DebugFormat(Environment.NewLine + "   Turn {0};Col:;{1};Pop:;{2};Morale:;{3};IntelProd;{9};IDef;{11};IAtt;{12};Maint;{10};Credits;{4};Change;{5};Research;{6};Dil;{14};Deut;{15};Dur;{16};{7};for;{8};{13};Owner;{17}" + Environment.NewLine
                    , GameContext.Current.TurnNumber

                    , civManager.Colonies.Count
                    , civManager.TotalPopulation
                    , civManager.AverageMorale
                    , civManager.Credits.CurrentValue
                    , civManager.Credits.CurrentChange
                    , civManager.Credits.LastChange

                    , civManager.Research.CumulativePoints
                    , civManager.Civilization.CivilizationType
                    , civManager.Civilization.Key
                    , civManager.TotalIntelligenceProduction
                    , civManager.MaintenanceCostLastTurn
                    , civManager.TotalIntelligenceDefenseAccumulated
                    , civManager.TotalIntelligenceAttackingAccumulated
                    , civManager.CivilizationID
                    , civManager.Resources.Dilithium.CurrentValue
                    , civManager.Resources.Deuterium.CurrentValue
                    , civManager.Resources.RawMaterials.CurrentValue
                    , civManager.HomeSystem.Owner.Key
                    //, civManager.Civilization.IntelOrdersGoingToHost.Count
                    //, civManager.Treasury.GrossIncome  // ;Treasury;{7}  // doesn't work, maybe it's just done with Credits !
                    //, civManager.Treasury.Maintenance  // ;Maint;{8}

                    );
            }

            GameContext.Current.TurnNumber++;
            _gameTurnNumber = GameContext.Current.TurnNumber;
        }
        #endregion

        #region DoAIPlayers() Method
        public void DoAIPlayers(object gameContext, List<Civilization> autoTurnCiv)
        {
            ConcurrentStack<Exception> errors = new System.Collections.Concurrent.ConcurrentStack<Exception>();
            if (!(gameContext is GameContext game))
                throw new ArgumentException("gameContext must be a valid GameContext instance");

            GameContext.PushThreadContext(game);

            try
            {
                ParallelForEach(game.Civilizations, civ =>
                {
                    GameContext.PushThreadContext(game);

                    try
                    {
                        //if (civ.IsHuman)
                        //    DiplomacyHelper.AcceptingRejecting(civ); // read accept reject dictionary for entry involving this civ
                        if (civ.IsHuman && !autoTurnCiv.Contains(civ))
                            return;

                        CivilizationManager civManager = GameContext.Current.CivilizationManagers[civ];
                        civManager.DesiredBorders = PlayerAI.CreateDesiredBorders(civ);

                        DiplomatAI.DoTurn(civ);
                        try
                        {
                            if (DiplomacyHelper.IsIndependent(civ))
                            {
                                ColonyAI.DoTurn(civ);

                                PlayerAI.DoTurn(civ);
                                UnitAI.DoTurn(civ);
                            }
                        }
                        catch (Exception e)
                        {
                            GameLog.Core.General.Error(e);
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Push(e);
                    }
                    finally
                    {
                        GameContext.PopThreadContext();
                    }
                });
            }
            finally
            {
                GameContext.PopThreadContext();
            }

            if (!errors.IsEmpty)
            {
                //throw new AggregateException(errors);
            }
        }
        #endregion

        #region OnCombatOccurring() Method
        /// <summary>
        /// Raises the <see cref="CombatOccurring"/> event.
        /// </summary>
        /// <param name="combat">The combat assets.</param>
        private void OnCombatOccurring(List<CombatAssets> combat)
        {
            if (combat == null)
                throw new ArgumentNullException("combat");

            for (int i = 0; i < combat.Count(); i++)
            {
                string _report = "Combat: ";
                CivilizationManager civManager = GameContext.Current.CivilizationManagers[combat[i].OwnerID];
                civManager.SitRepEntries.Add(new CombatSummarySitRepEntry(combat[i].Owner, combat[i].Location, _report));
            }

            CombatOccurring?.Invoke(combat);

        }
        #endregion

        #region OnInvasionOccurring() Method
        /// <summary>
        /// 
        /// Raises the <see cref="InvasionOccurring"/> event.
        /// </summary>
        /// <param name="invasionArena">The invasion arena.</param>
        private void OnInvasionOccurring(InvasionArena invasionArena)
        {
            if (invasionArena == null)
                throw new ArgumentNullException("invasionArena");

            InvasionOccurring?.Invoke(invasionArena);
        }
        #endregion

        #region NotifyCombatFinished() Method
        /// <summary>
        /// Resets the combat wait handle.
        /// </summary>
        public void NotifyCombatFinished()
        {
            CombatReset.Set();
        }
        #endregion


        // ReSharper disable UnusedMethodReturnValue.Local
        private static ParallelLoopResult ParallelForEach<TSource>(
            [NotNull] IEnumerable<TSource> source,
            [NotNull] Action<TSource> body)
        {
            return Parallel.ForEach(
                source,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 4 },
                body);
        }

        //    civManager.AddCivHist(civManager.CivilizationID
        //, civManager.Civilization.Key
        //, civManager.Credits.CurrentValue
        //, civManager.Colonies.Count
        //, _totalPopulation
        //, civManager.MaintenanceCostLastTurn
        //, _totalResearch
        //);
        //            // works - just for DEBUG  // optimized for CSV-Export (CopyPaste)
        //            GameLog.Core.CivsAndRaces.DebugFormat(Environment.NewLine + "   Turn {0};Col:;{1};Pop:;{2};Morale:;{3};IntelProd;{9};IDef;{11};IAtt;{12};Maint;{10};Credits;{4};Change;{5};Research;{6};Dil;{14};Deut;{15};Dur;{16};{7};for;{8};{13};Owner;{17}" + Environment.NewLine
        //                , GameContext.Current.TurnNumber                    

        //                , civManager.Colonies.Count
        //                , civManager.TotalPopulation
        //                , civManager.AverageMorale
        //                , civManager.Credits.CurrentValue
        //                , civManager.Credits.CurrentChange
        //                , civManager.Credits.LastChange

        //                , civManager.Research.CumulativePoints
        //                , civManager.Civilization.CivilizationType
        //                , civManager.Civilization.Key
        //                , civManager.TotalIntelligenceProduction
        //                , civManager.MaintenanceCostLastTurn
        //                , civManager.TotalIntelligenceDefenseAccumulated
        //                , civManager.TotalIntelligenceAttackingAccumulated
        //                , civManager.CivilizationID
        //                , civManager.Resources.Dilithium.CurrentValue
        //                , civManager.Resources.Deuterium.CurrentValue
        //                , civManager.Resources.RawMaterials.CurrentValue
        //                , civManager.HomeSystem.Owner.Key



        // new list/stuff to find out 'Best' and 'Places' values for each civ
        public class CivValue
        {
            public int AA_CIV_ID;
            public string CIV_KEY;
            public Meter CIV_TOT_POP;

            public int CIV_TOT_INT;


            public CivValue(
                int aa_civ_ID
                , string civ_key
                , Meter civ_tot_pop
                , int civ_tot_int
                )
            {
                AA_CIV_ID = aa_civ_ID;
                CIV_KEY = civ_key;
                CIV_TOT_POP = civ_tot_pop;
                CIV_TOT_INT = civ_tot_int;
            }
        }

        public void AddCivValue(int civID, string civKey, Meter civTotPop, int civTotInt)
        {
            CivValue civValueNew = new CivValue(
                civID
                , civKey
                , civTotPop
                , civTotInt
                );
            CivValueList.Add(civValueNew);
        }
        // ReSharper restore UnusedMethodReturnValue.Local
        //public void GetAcceptReject(ForeignPower foreignPower)
        //{
        //    if (foreignPower.PendingAction == PendingDiplomacyAction.AcceptProposal)
        //        AcceptProposalVisitor.Visit(foreignPower.LastProposalReceived);
        //    else RejectProposalVisitor.Visit(foreignPower.LastProposalReceived); 
        //}
    }

    /// <summary>
    /// Defines the turn processing phases used by the game engine.
    /// </summary>
    public enum TurnPhase : byte
    {
        WaitOnPlayers = 0,
        PreTurnOperations,
        // SpyOperations,
        ResetObjects,
        FleetMovement,
        Combat,
        PopulationGrowth,
        Research,
        Scrapping,
        Maintenance,
        ShipProduction,
        Production,
        Trade,
        // Intelligence,
        Morale,
        MapUpdates,
        PostTurnOperations,
        SendUpdates,
        Diplomacy,
        WaitOnAIPlayers
    }

    /// <summary>
    /// Delegate used for event handlers related to changes in the current turn phase.
    /// </summary>
    public delegate void TurnPhaseEventHandler(TurnPhase phase);

    /// <summary>
    /// Delegate used for event handlers related to the initiation of combat.
    /// </summary>
    public delegate void CombatEventHandler(List<CombatAssets> assets);

    /// <summary>
    /// Delegate used for event handlers related to the initiation of system invasions.
    /// </summary>
    public delegate void InvasionEventHandler(InvasionArena invasionArena);
}
