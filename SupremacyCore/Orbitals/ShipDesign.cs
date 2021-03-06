// ShipDesign.cs
//
// Copyright (c) 2007 Mike Strobel
//
// This source code is subject to the terms of the Microsoft Reciprocal License (Ms-RL).
// For details, see <http://www.opensource.org/licenses/ms-rl.html>.
//
// All other rights reserved.

using System;
using System.Reflection;
using System.Xml;

using Supremacy.Economy;
using Supremacy.Encyclopedia;
using Supremacy.Entities;
using Supremacy.Game;
using Supremacy.Tech;
using Supremacy.Types;
using Supremacy.Universe;
using System.Collections.Generic;
using Supremacy.Utility;
using System.Linq;

namespace Supremacy.Orbitals
{
    [Serializable]
    public class ShipDesign : OrbitalDesign
    {
        private byte _speed;
        private byte _range;
        private byte _fuelCapacity;
        private byte _cloakStrength;
        private byte _camouflagedStrength;
        private byte _maneuverability;
        private byte _evacuationLimit;
        private ushort _workCapacity;
        private Percentage _interceptAbility;
        private Percentage _raidAbility;
        private Dictionary<string, int> _possibleNames;
        private ShipType _shipClass;

        /// <summary>
        /// Gets or sets the ship class name.
        /// </summary>
        /// <value>The ship class name.</value>
        public string ClassName { get; set; }

        /// <summary>
        /// Gets a value indicating whether this <see cref="ShipDesign"/> is combatant.
        /// </summary>
        /// <value>
        /// <c>true</c> if this <see cref="ShipDesign"/> is combatant; otherwise, <c>false</c>.
        /// </value>
        public override bool IsCombatant
        {
            get
            {
                if (base.IsCombatant)
                    return true;
                FieldInfo fieldInfo = typeof(ShipType).GetField(_shipClass.ToString());
                if (fieldInfo != null)
                    return !Attribute.IsDefined(fieldInfo, typeof(NonCombatantAttribute));
                return false;
            }
        }

        /// <summary>
        /// Gets or sets the intercept ability.
        /// </summary>
        /// <value>The intercept ability.</value>
        public Percentage InterceptAbility
        {
            get { return _interceptAbility; }
            set { _interceptAbility = value; }
        }

        /// <summary>
        /// Gets or sets the raid ability.
        /// </summary>
        /// <value>The raid ability.</value>
        public Percentage RaidAbility
        {
            get { return _raidAbility; }
            set { _raidAbility = value; }
        }

        /// <summary>
        /// Gets or sets the evacuation limit.
        /// </summary>
        /// <value>The evacuation limit.</value>
        public int EvacuationLimit
        {
            get { return _evacuationLimit; }
            set { _evacuationLimit = (byte)Math.Max(0, Math.Min(value, Byte.MaxValue)); }
        }

        /// <summary>
        /// Gets or sets the work capacity.
        /// </summary>
        /// <value>The work capacity.</value>
        /// <remarks>
        /// The work capacity value is used for different purposes based on the type of ship.
        /// For a colony ship, it represents the maximum initial population that can be settled
        /// on a new colony; for a transport ship, it represents capacity; etc.
        /// </remarks>
        public int WorkCapacity
        {
            get { return _workCapacity; }
            set { _workCapacity = (ushort)Math.Max(0, Math.Min(value, UInt16.MaxValue)); }
        }

        /// <summary>
        /// Gets or sets the maneuverability rating.
        /// </summary>
        /// <value>The maneuverability rating.</value>
        public new int Maneuverability
        {
            get { return _maneuverability; }
            set { _maneuverability = (byte)Math.Max(0, Math.Min(value, Byte.MaxValue)); }
        }

        /// <summary>
        /// Gets or sets the speed.
        /// </summary>
        /// <value>The speed.</value>
        public int Speed
        {
            get { return _speed; }
            set { _speed = (byte)value; }
        }

        /// <summary>
        /// Gets or sets the range.
        /// </summary>
        /// <value>The range.</value>
        public int Range
        {
            get { return _range; }
            set { _range = (byte)value; }
        }

        /// <summary>
        /// Gets or sets the fuel capacity.
        /// </summary>
        /// <value>The fuel capacity.</value>
        public int FuelCapacity
        {
            get { return _fuelCapacity; }
            set { _fuelCapacity = (byte)value; }
        }

        /// <summary>
        /// Gets or sets the cloak strength.
        /// </summary>
        /// <value>The cloak strength.</value>
        public new int CloakStrength
        {
            get { return _cloakStrength; }
            set { _cloakStrength = (byte)value; }
        }

        /// <summary>
        /// Gets or sets the camouflaged strength.
        /// </summary>
        /// <value>The camouflaged strength.</value>
        public new int CamouflagedStrength
        {
            get { return _camouflagedStrength; }
            set { _camouflagedStrength = (byte)value; }
        }
        /// <summary>
        /// Gets or sets the type of the ship.
        /// </summary>
        /// <value>The type of the ship.</value>
        public new ShipType ShipType
        {
            get { return _shipClass; }
            set { _shipClass = value; }
        }

        /// <summary>
        /// Gets or sets the dilithium cost.
        /// </summary>
        /// <value>The dilithium cost.</value>
        public int Dilithium
        {
            get { return BuildResourceCosts[ResourceType.Dilithium]; }
            set { BuildResourceCosts[ResourceType.Dilithium] = value; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShipDesign"/> class.
        /// </summary>
        public ShipDesign() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShipDesign"/> class using XML data.
        /// </summary>
        /// <param name="element">The XML element.</param>
        public ShipDesign(XmlElement element) : base(element)
        {
            _possibleNames = new Dictionary<string, int>();

            if (element["ShipType"] != null)
            {
                _shipClass = (ShipType)Enum.Parse(
                    typeof(ShipType), element["ShipType"].InnerText.Trim());
            }
            if (element["ClassName"] != null)
            {
                ClassName = element["ClassName"].InnerText.Trim();
            }
            if (element["BuildCost"] != null)  // this is not special for Ships - but just checking value here
            {
                //if (Number.ParseInt32(element["BuildCost"].InnerText.Trim() == 0))
                //    GameLog.Core.GameData.DebugFormat("In TechObjectDatabase.xml for {0}: BuildCost should not be 0", 
                //        Number.ParseInt32(element["BuildCost"].InnerText.Trim()));
            }
            if (element["Dilithium"] != null)
            {
                BuildResourceCosts[ResourceType.Dilithium] =
                    Number.ParseInt32(element["Dilithium"].InnerText.Trim());
                if (BuildResourceCosts[ResourceType.Dilithium] < 1)
                    GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: Dilithium should not be 0", Name);
            }
            if (element["CloakStrength"] != null)
            {
                _cloakStrength = Number.ParseByte(element["CloakStrength"].InnerText.Trim());
                if (_cloakStrength != 0)
                    if (_cloakStrength < 4 || _cloakStrength > 20)   // atm all values between 6 and 18 (or 0 for not having this ability)
                        GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _cloakStrength should not be {1}", Name, _cloakStrength);
            }
            if (element["CamouflagedStrength"] != null)
            {
                _camouflagedStrength = Number.ParseByte(element["CamouflagedStrength"].InnerText.Trim());
                if (_camouflagedStrength != 0)
                    if (_camouflagedStrength < 7 || _camouflagedStrength > 9)   // atm all values between 7 and 9 (or 0 for not having this ability)
                        GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _camouflagedStrength should not be {1}", Name, _camouflagedStrength);
            }
            //if (element["RawMaterials"] != null)
            //{
            //    BuildResourceCosts[ResourceType.RawMaterials] =
            //        ParseInt32(element["RawMaterials"].InnerText.Trim());
            //}
            if (element["Range"] != null)
            {
                _range = Number.ParseByte(element["Range"].InnerText.Trim());
                if (_range == 0 || _range > 25)  // atm 25 is highest value
                    GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _range should not be {1}", Name, _range);
            }
            if (element["Speed"] != null)
            {
                _speed = Number.ParseByte(element["Speed"].InnerText.Trim());
                if (_speed == 0 || _speed > 15)
                    GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _speed should not be {1}", Name, _speed);
            }
            if (element["FuelReserve"] != null)
            {
                _fuelCapacity = Number.ParseByte(element["FuelReserve"].InnerText.Trim());
                //BuildResourceCosts[ResourceType.Deuterium] = _fuelCapacity;

                if (_fuelCapacity > 15)   // atm Empires have 4 and minors have a zero
                    GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _fuelCapacity should not be {1}", Name, _fuelCapacity);
            }
            if (element["InterceptAbility"] != null)
            {
                _interceptAbility = Number.ParsePercentage(element["InterceptAbility"].InnerText.Trim());
                if (_interceptAbility != 0)
                    if (_interceptAbility * 100 < 1 || _interceptAbility * 100 > 99)   // atm all values between 0% and 45% (or 0 for not having this ability)
                        GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _interceptAbility should not be {1}", Name, _interceptAbility);
            }
            if (element["RaidAbility"] != null)
            {
                _interceptAbility = Number.ParsePercentage(element["RaidAbility"].InnerText.Trim());
            }
            if (element["Maneuverability"] != null)
            {
                _maneuverability = Number.ParseByte(element["Maneuverability"].InnerText.Trim());
                if (_maneuverability != 0)
                    if (_maneuverability < 1 || _maneuverability > 12)   // atm all values between 1 and 10 (or 0 for not having this ability)
                        GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _maneuverability should not be {1}", Name, _maneuverability);
            }
            if (element["EvacuationLimit"] != null)
            {
                _evacuationLimit = Number.ParseByte(element["EvacuationLimit"].InnerText.Trim());
                if (_evacuationLimit != 0)
                    if (_evacuationLimit < 1 || _evacuationLimit > 12)   // atm all values between 1 and 10 (or 0 for not having this ability)
                        GameLog.Core.GameData.WarnFormat("In TechObjectDatabase.xml for {0}: _evacuationLimit should not be {1}", Name, _evacuationLimit);
            }
            if (element["WorkCapacity"] != null)
            {
                _workCapacity = Number.ParseUInt16(element["WorkCapacity"].InnerText.Trim());
            }
            if (element["ShipNames"] == null)
            {
                GameLog.Core.GameData.WarnFormat("ShipNames missing in TechObjectDatabase.xml for {0}", Name);
            }
            else
            {
                // for problems with ships activate ...
                //GameLog.Core.GameData.DebugFormat("ShipNames available (see TechObjectDatabase.xml or activate FullOutput in code) for {0}", Name);

                foreach (XmlElement name in element["ShipNames"])
                {
                    _possibleNames.Add(name.InnerText.Trim(), 0);
                    //GameLog.Core.GameData.DebugFormat("ShipNames - Possible Name for {0} = {1}", Name, name.InnerText.Trim());
                }
            }
        }

        protected override string DefaultImageSubFolder => "Ships/";

        protected override string DefaultShipsUnderConstructionSubFolder => "Ships_Under_Construction/";

        /// <summary>
        /// Appends the XML data for this instance.
        /// </summary>
        /// <param name="baseElement">The base XML element.</param>
        protected internal override void AppendXml(XmlElement baseElement)
        {
            base.AppendXml(baseElement);

            XmlDocument doc = baseElement.OwnerDocument;
            XmlElement newElement;

            newElement = doc.CreateElement("ShipType");
            newElement.InnerText = ShipType.ToString();
            baseElement.AppendChild(newElement);

            newElement = doc.CreateElement("ClassName");
            newElement.InnerText = ClassName;
            baseElement.AppendChild(newElement);

            if (Dilithium > 0)
            {
                newElement = doc.CreateElement("Dilithium");
                newElement.InnerText = Dilithium.ToString();
                baseElement.AppendChild(newElement);
            }

            if (CloakStrength > 0)
            {
                newElement = doc.CreateElement("CloakStrength");
                newElement.InnerText = CloakStrength.ToString();
                baseElement.AppendChild(newElement);
            }

            if (base.CamouflagedStrength > 0)
            {
                newElement = doc.CreateElement("CamouflagedStrength");
                newElement.InnerText = base.CamouflagedStrength.ToString();
                baseElement.AppendChild(newElement);
            }

            newElement = doc.CreateElement("Range");
            newElement.InnerText = Range.ToString();
            baseElement.AppendChild(newElement);

            newElement = doc.CreateElement("Speed");
            newElement.InnerText = Speed.ToString();
            baseElement.AppendChild(newElement);

            newElement = doc.CreateElement("FuelReserve");
            newElement.InnerText = FuelCapacity.ToString();
            baseElement.AppendChild(newElement);

            if (InterceptAbility > 0)
            {
                newElement = doc.CreateElement("InterceptAbility");
                newElement.InnerText = _interceptAbility.ToString();
                baseElement.AppendChild(newElement);
            }

            if (RaidAbility > 0)
            {
                newElement = doc.CreateElement("RaidAbility");
                newElement.InnerText = _raidAbility.ToString();
                baseElement.AppendChild(newElement);
            }

            if (Maneuverability > 0)
            {
                newElement = doc.CreateElement("Maneuverability");
                newElement.InnerText = Maneuverability.ToString();
                baseElement.AppendChild(newElement);
            }

            if (EvacuationLimit > 0)
            {
                newElement = doc.CreateElement("EvacuationLimit");
                newElement.InnerText = EvacuationLimit.ToString();
                baseElement.AppendChild(newElement);
            }

            if (WorkCapacity > 0)
            {
                newElement = doc.CreateElement("WorkCapacity");
                newElement.InnerText = WorkCapacity.ToString();
                baseElement.AppendChild(newElement);
            }
            if (_possibleNames.Count > 0)
            {
                newElement = doc.CreateElement("ShipNames");
                foreach (KeyValuePair<string, int> shipName in _possibleNames)
                {
                    XmlElement nameElement = doc.CreateElement("ShipName");
                    nameElement.InnerText = shipName.Key;
                    newElement.AppendChild(nameElement);
                }
                baseElement.AppendChild(newElement);
            }
        }

        /// <summary>
        /// Spawns an instance of an object of this design at the specified location.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <param name="owner">The owner.</param>
        /// <param name="spawnedInstance"> </param>
        public override bool TrySpawn(MapLocation location, Civilization owner, out TechObject spawnedInstance)
        {
            if (!CanSpawn(location, owner))
            {
                spawnedInstance = null;
                return false;
            }

            CivilizationManager civManager = GameContext.Current.CivilizationManagers[owner];
            Ship ship = new Ship(this);

            string shipDesign = ship.ShipDesign.Name;


            if (TechTreeHelper.MeetsTechLevels(civManager, ship.ShipDesign) != true && civManager.Civilization.IsEmpire)  // minors > MeetsTechLevel doesn't work fine
            {
                GameLog.Core.GameData.DebugFormat("{0}, {1}, {2}, {3}, {4}, {5}, ship highest tech level is {6} for {7}, exceeding current Techlevel",

                    ship.ShipDesign.TechRequirements[TechCategory.BioTech],
                    ship.ShipDesign.TechRequirements[TechCategory.Computers],
                    ship.ShipDesign.TechRequirements[TechCategory.Construction],
                    ship.ShipDesign.TechRequirements[TechCategory.Energy],
                    ship.ShipDesign.TechRequirements[TechCategory.Propulsion],
                    ship.ShipDesign.TechRequirements[TechCategory.Weapons],
                    ship.ShipDesign.TechRequirements.HighestTechLevel,
                    ship
                    );
            }

            ship.Owner = owner;
            //If we have any possible names for this ship class, pick one
            if (_possibleNames.Count > 0)
            {
                //Set this to -1 so we can check if we've checked any yet
                int timesUsed = -1;
                string leastUsedName = "";
                foreach (KeyValuePair<string, int> shipName in _possibleNames)
                {
                    //If we haven't checked, assign this straight to the variables
                    if (timesUsed == -1)
                    {
                        timesUsed = shipName.Value;
                        leastUsedName = shipName.Key;
                    }
                    else
                    {
                        //Check to see if this name has been used less than the one in the variable
                        if (shipName.Value < timesUsed)
                        {
                            timesUsed = shipName.Value;
                            leastUsedName = shipName.Key;
                        }
                    }
                }
                string newShipName = "";
                if (owner.ShipPrefix != null)
                    newShipName = owner.ShipPrefix + " ";
                newShipName = newShipName + leastUsedName;
                if (ship.Owner.Key == "BORG")
                {
                    newShipName = newShipName + " " + ShipSuffixes.Binary(timesUsed + 1).PadLeft(4, '0');
                }
                else
                {
                    if (timesUsed > 0)
                    {
                        newShipName = newShipName + " " + ShipSuffixes.Alphabetical(timesUsed);
                    }
                }

                ship.Name = newShipName;

                _possibleNames[leastUsedName] = timesUsed + 1;
            }
            ship.Reset();
            ship.Location = location;
            ship.CreateFleet();

            int fuelNeeded = ship.FuelReserve.Maximum - ship.FuelReserve.CurrentValue;
            if (fuelNeeded > 0)
                ship.FuelReserve.AdjustCurrent(civManager.Resources[ResourceType.Deuterium].AdjustCurrent(-fuelNeeded));

            // default we want to be "camouflaged"
            if (ship.CanCamouflage == true && ship.IsCamouflaged == false)
                ship.IsCamouflaged = true;

            GameContext.Current.Universe.Objects.Add(ship);

            civManager.MapData.SetExplored(location, true);
            civManager.MapData.SetScanned(location, true, SensorRange);
            civManager.MapData.UpgradeScanStrength(location, ScanStrength, SensorRange);

            spawnedInstance = ship;
            return true;
        }

        /// <summary>
        /// Gets the encyclopedia category under which the entry appears.
        /// </summary>
        /// <value>The encyclopedia category.</value>
        public override EncyclopediaCategory EncyclopediaCategory => EncyclopediaCategory.Ships;
    }
}