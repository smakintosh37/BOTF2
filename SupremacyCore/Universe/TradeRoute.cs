// File:TradeRoute.cs
//
// Copyright (c) 2007 Mike Strobel
//
// This source code is subject to the terms of the Microsoft Reciprocal License (Ms-RL).
// For details, see <http://www.opensource.org/licenses/ms-rl.html>.
//
// All other rights reserved.

using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.Practices.ServiceLocation;
using Supremacy.Diplomacy;
using Supremacy.Economy;
using Supremacy.Entities;
using Supremacy.Game;
using Supremacy.Types;
using Supremacy.Utility;

namespace Supremacy.Universe
{
    /// <summary>
    /// Represents a trade route between two colonies.
    /// </summary>
    [Serializable]
    public class TradeRoute : Cloneable, INotifyPropertyChanged, ICloneable
    {
        private readonly int _sourceColonyId;
        //private readonly MapLocation _tradeRouteLocation;
        private int _targetColonyId;
        private int _credits;

        /// <summary>
        /// Gets the <see cref="Colony"/> from which the <see cref="TradeRoute"/> originates.
        /// <value>The source colony.</value>
        /// </summary>
        public Colony SourceColony => GameContext.Current.Universe.Objects[_sourceColonyId] as Colony;

        //public MapLocation TradeRouteLocation   
        //{

        //   get { return GameContext.Current.Universe.FindAt<StarSystem>(Loc); }
        //}

        /// <summary>
        /// Gets or sets the <see cref="Colony"/> at which the <see cref="TradeRoute"/> terminates.
        /// </summary>
        /// <value>The target colony.</value>
        public Colony TargetColony
        {
            get { return GameContext.Current.Universe.Objects[_targetColonyId] as Colony; }
            set
            {
                _targetColonyId = (value != null)
                    ? value.ObjectID
                    : -1;

                foreach (TradeRoute route in SourceColony.TradeRoutes)
                {
                    if ((route != this) && route.IsAssigned
                        && (route.TargetColony == TargetColony))
                    {
                        route.TargetColony = null;
                    }
                }

                OnPropertyChanged("TargetColony");

                if (value == null)
                {
                    Credits = 0;
                }
                else
                {
                    float baseModSource = 0.025f;
                    float baseModTarget = 0.05f;

                    Data.Table baseResProdTable = GameContext.Current.Tables.ResourceTables["TradeRoutePopMultipliers"];
                    if (baseResProdTable != null)
                    {
                        try
                        {
                            float.TryParse(baseResProdTable["Source"]["Value"], out float modSrc);
                            float.TryParse(baseResProdTable["Target"]["Value"], out float modTarget);

                            baseModSource = modSrc;
                            baseModTarget = modTarget;
                        }
                        catch (Exception e)
                        {
                            GameLog.Core.General.Error("#### problem with TradeRoute-TargetColony", e);
                        }
                    }

                    Credits = (int)(baseModSource * SourceColony.Population.CurrentValue +
                                    baseModTarget * TargetColony.Population.CurrentValue);
                    GameLog.Core.General.DebugFormat("colony is null, TurnNumber={0}, Credits by TradeRoute={1}", GameContext.Current.TurnNumber, Credits);
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="TargetColony"/> is assigned.
        /// </summary>
        /// <value>
        /// <c>true</c> if <see cref="TargetColony"/> is assigned; otherwise, <c>false</c>.
        /// </value>
        public bool IsAssigned =>
                // works
                // GameLog.Core.TradeRoutes.DebugFormat("IsAssigned ={0}", (TargetColony != null)); //, TargetColony.Owner);
                TargetColony != null;

        /// <summary>
        /// Gets or sets the credits generated by a <see cref="TradeRoute"/>.
        /// </summary>
        /// <value>The credits.</value>
        public int Credits
        {
            get { return _credits; }
            set
            {
                _credits = value;
                OnPropertyChanged("Credits");
            }
        }

        public int LocalPlayerCredits
        {
            get
            {
                if (!IsAssigned)
                    return 0;

                //var clientContext = ServiceLocator.Current.GetInstance<IClientContext>();
                try
                {
                    IPlayer clientContext = ServiceLocator.Current.GetInstance<IPlayer>();
                    //var clientContext = Game.IPlayer.
                    if (clientContext == null)
                    {
                        GameLog.Core.General.DebugFormat("clientContext is null, TurnNumber={0}, Credits by TradeRoute={1}", GameContext.Current.TurnNumber, Credits);
                        return Credits;
                    }
                    Civilization empire = clientContext.Empire;//.LocalPlayer.Empire;
                    if (empire == null)
                    {
                        GameLog.Core.General.DebugFormat("empire is null, TurnNumber={0}, Credits by TradeRoute={1}", GameContext.Current.TurnNumber, Credits);
                        return Credits;
                    }

                    Colony colony;

                    if (SourceColony.OwnerID == empire.CivID)
                        colony = SourceColony;
                    else if (TargetColony.OwnerID == empire.CivID)
                        colony = TargetColony;
                    else
                        colony = null;

                    if (colony == null)
                    {
                        GameLog.Core.General.DebugFormat("colony is null, TurnNumber={0}, Credits by TradeRoute={1}", GameContext.Current.TurnNumber, Credits);
                        return Credits;
                    }

                    double bonus = colony.Buildings
                                      .Where(o => o.IsActive)
                                      .SelectMany(o => o.BuildingDesign.Bonuses)
                                      .Where(o => o.BonusType == BonusType.PercentTradeIncome)
                                      .Sum(o => 0.01 * o.Amount);

                    GameLog.Core.General.DebugFormat("Turn {0}, Credits from TradeRoute (incl. Bonuses): Credits by TradeRoute={1}", GameContext.Current.TurnNumber, Credits);
                    return (int)((1.0 + bonus) * Credits);
                }
                catch (Exception e)
                {
                    GameLog.Core.General.ErrorFormat(string.Format(Environment.NewLine + "   #### problem with TradeRoute - ServiceLocator.Current.GetInstance<IClientContext>(), returning {0} credits",
                        Credits),
                        e);
                    return Credits;
                }

                //var empire = clientContext.LocalPlayer.Empire;
                //if (empire == null)
                //{
                //    GameLog.Print("empire is null, TurnNumber={0}, Credits by TradeRoute={1}", GameContext.Current.TurnNumber, this.Credits);
                //    return this.Credits;
                //}

                //Colony colony;

                //if (this.SourceColony.OwnerID == empire.CivID)
                //    colony = this.SourceColony;
                //else if (this.TargetColony.OwnerID == empire.CivID)
                //    colony = this.TargetColony;
                //else
                //    colony = null;

                //if (colony == null)
                //{
                //    GameLog.Print("colony is null, TurnNumber={0}, Credits by TradeRoute={1}", GameContext.Current.TurnNumber, this.Credits);
                //    return this.Credits;
                //}

                //var bonus = colony.Buildings
                //                  .Where(o => o.IsActive)
                //                  .SelectMany(o => o.BuildingDesign.Bonuses)
                //                  .Where(o => o.BonusType == BonusType.PercentTradeIncome)
                //                  .Sum(o => 0.01 * o.Amount);

                //GameLog.Print("Credits from TradeRoute (incl. Bonuses): TurnNumber={0}, Credits by TradeRoute={1}", GameContext.Current.TurnNumber, this.Credits);
                //return (int)((1.0 + bonus) * this.Credits);
            }
        }

        public Civilization Owner { get; set; }

        internal void NotifyLocalPlayerCreditsChanged()
        {
            OnPropertyChanged("LocalPlayerCredits");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TradeRoute"/> class.
        /// </summary>
        /// <param name="sourceColony">
        /// The colony at which the <see cref="TradeRoute"/> originates.
        /// </param>
        public TradeRoute(Colony sourceColony)
        {
            if (sourceColony == null)
                throw new ArgumentNullException("sourceColony");
            _sourceColonyId = sourceColony.ObjectID;
            _targetColonyId = -1;
            _credits = 0;
        }


        /// <summary>
        /// Determines whether a <see cref="Colony"/> is a valid target for a <see cref="TradeRoute"/>.
        /// </summary>
        /// <param name="colony">The colony.</param>
        /// <returns>
        /// <c>true</c> if [is valid target colony] [the specified colony]; otherwise, <c>false</c>.
        /// </returns>
        public bool IsValidTargetColony(Colony colony)
        {
            if (colony == null)
                return true;
            if (!colony.IsOwned)
                return false;
            if (colony.OwnerID == SourceColony.OwnerID)
                return false;
            return DiplomacyHelper.IsTradeEstablished(colony.Owner, SourceColony.Owner);
        }

        #region INotifyPropertyChanged Members
        /// <summary>
        /// Notifies clients that a property value has changed.
        /// </summary>
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">Name of the property that has changed.</param>
        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Implementation of ICloneable

        object ICloneable.Clone()
        {
            return Clone();
        }

        public TradeRoute Clone()
        {
            return Cloneable.Clone(this);
        }

        #endregion

        #region Implementation of ICloneable<TradeRoute>

        protected override Cloneable CreateInstance(ICloneContext context)
        {
            return new TradeRoute(SourceColony);
        }

        public override void CloneFrom(Cloneable original, ICloneContext context)
        {
            TradeRoute source = (TradeRoute)original;
            _targetColonyId = source._targetColonyId;
            _credits = source._credits;
        }

        #endregion
    }
}
