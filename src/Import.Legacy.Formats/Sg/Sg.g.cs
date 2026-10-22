#nullable disable
// This is a generated file! Please edit source .ksy file and use kaitai-struct-compiler to rebuild

using System.Collections.Generic;
using System;

namespace Kaitai
{

    /// <summary>
    /// SG is used by [Disciples 2](https://en.wikipedia.org/wiki/Disciples_II:_Dark_Prophecy) game for scenario maps and saves.
    /// 
    /// Written and tested by Vladimir Makeev, 2019-2020
    /// </summary>
    public partial class Sg : KaitaiStruct
    {
        public static Sg FromFile(string fileName)
        {
            return new Sg(new KaitaiStream(fileName));
        }

        public Sg(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
        {
            m_parent = p__parent;
            m_root = p__root ?? this;
            f_newBody = false;
            _read();
        }
        private void _read()
        {
            _header = new SgHeader(m_io, this, m_root);
            _total = new IntRecord("SxxxOBxxxx", m_io, this, m_root);
            _objects = new List<ScenarioObject>(SgRead.Capacity(Total.Value));
            for (var i = 0; i < Total.Value; i++)
            {
                _objects.Add(new ScenarioObject(m_io, this, m_root));
            }
        }

        /// <summary>
        /// Represents ingame currency
        /// </summary>
        public partial class BankRecord : KaitaiStruct
        {
            public BankRecord(string p_nameValue, KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _nameValue = p_nameValue;
                _read();
            }
            private void _read()
            {
                _name = SgRead.Name(m_io, NameValue);
                _length = m_io.ReadUInt32();
                _gold = new Currency(m_io, this, m_root);
                _colon1 = m_io.ReadBytes(1);
                if (!((KaitaiStream.ByteArrayCompare(_colon1, new byte[] { 58 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 58 }, _colon1, m_io, "/types/bank_record/seq/3");
                }
                _infernalMana = new Currency(m_io, this, m_root);
                _colon2 = m_io.ReadBytes(1);
                if (!((KaitaiStream.ByteArrayCompare(_colon2, new byte[] { 58 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 58 }, _colon2, m_io, "/types/bank_record/seq/5");
                }
                _lifeMana = new Currency(m_io, this, m_root);
                _colon3 = m_io.ReadBytes(1);
                if (!((KaitaiStream.ByteArrayCompare(_colon3, new byte[] { 58 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 58 }, _colon3, m_io, "/types/bank_record/seq/7");
                }
                _deathMana = new Currency(m_io, this, m_root);
                _colon4 = m_io.ReadBytes(1);
                if (!((KaitaiStream.ByteArrayCompare(_colon4, new byte[] { 58 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 58 }, _colon4, m_io, "/types/bank_record/seq/9");
                }
                _runicMana = new Currency(m_io, this, m_root);
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _colon5 = m_io.ReadBytes(1);
                    if (!((KaitaiStream.ByteArrayCompare(_colon5, new byte[] { 58 }) == 0)))
                    {
                        throw new ValidationNotEqualError(new byte[] { 58 }, _colon5, m_io, "/types/bank_record/seq/11");
                    }
                }
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _groveMana = new Currency(m_io, this, m_root);
                }
                _terminator = m_io.ReadU1();
            }
            private string _name;
            private uint _length;
            private Currency _gold;
            private byte[] _colon1;
            private Currency _infernalMana;
            private byte[] _colon2;
            private Currency _lifeMana;
            private byte[] _colon3;
            private Currency _deathMana;
            private byte[] _colon4;
            private Currency _runicMana;
            private byte[] _colon5;
            private Currency _groveMana;
            private byte _terminator;
            private string _nameValue;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public string Name { get { return _name; } }
            public uint Length { get { return _length; } }
            public Currency Gold { get { return _gold; } }
            public byte[] Colon1 { get { return _colon1; } }

            /// <summary>
            /// Associated with and primarily used by Legions of the Damned
            /// </summary>
            public Currency InfernalMana { get { return _infernalMana; } }
            public byte[] Colon2 { get { return _colon2; } }

            /// <summary>
            /// Associated with and primarily used by Empire
            /// </summary>
            public Currency LifeMana { get { return _lifeMana; } }
            public byte[] Colon3 { get { return _colon3; } }

            /// <summary>
            /// Associated with and primarily used by Undead Hordes
            /// </summary>
            public Currency DeathMana { get { return _deathMana; } }
            public byte[] Colon4 { get { return _colon4; } }

            /// <summary>
            /// Associated with and primarily used by Mountain Clans
            /// </summary>
            public Currency RunicMana { get { return _runicMana; } }
            public byte[] Colon5 { get { return _colon5; } }

            /// <summary>
            /// Associated with and primarily used by Elven alliance
            /// </summary>
            public Currency GroveMana { get { return _groveMana; } }
            public byte Terminator { get { return _terminator; } }
            public string NameValue { get { return _nameValue; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class Begobject : KaitaiStruct
        {
            public static Begobject FromFile(string fileName)
            {
                return new Begobject(new KaitaiStream(fileName));
            }

            public Begobject(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _value = m_io.ReadBytes(10);
                if (!((KaitaiStream.ByteArrayCompare(_value, new byte[] { 66, 69, 71, 79, 66, 74, 69, 67, 84, 0 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 66, 69, 71, 79, 66, 74, 69, 67, 84, 0 }, _value, m_io, "/types/begobject/seq/0");
                }
            }
            private byte[] _value;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public byte[] Value { get { return _value; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class BoolRecord : KaitaiStruct
        {
            public BoolRecord(string p_nameValue, KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _nameValue = p_nameValue;
                _read();
            }
            private void _read()
            {
                _name = SgRead.Name(m_io, NameValue);
                _value = m_io.ReadU1();
            }
            private string _name;
            private byte _value;
            private string _nameValue;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public string Name { get { return _name; } }
            public byte Value { get { return _value; } }
            public string NameValue { get { return _nameValue; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class Capital : KaitaiStruct
        {
            public static Capital FromFile(string fileName)
            {
                return new Capital(new KaitaiStream(fileName));
            }

            public Capital(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _city = new City(m_io, this, m_root);
                _itemCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _items = new List<StringRecord>(SgRead.Capacity(ItemCount.Value));
                for (var i = 0; i < ItemCount.Value; i++)
                {
                    _items.Add(new StringRecord("XXXxxxX", m_io, this, m_root));
                }
                _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
            }
            private City _city;
            private IntRecord _itemCount;
            private List<StringRecord> _items;
            private IntRecord _aipriority;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public City City { get { return _city; } }
            public IntRecord ItemCount { get { return _itemCount; } }
            public List<StringRecord> Items { get { return _items; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class City : KaitaiStruct
        {
            public static City FromFile(string fileName)
            {
                return new City(new KaitaiStream(fileName));
            }

            public City(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _cityId = new StringRecord("CITY_ID", m_io, this, m_root);
                _nameTxt = new StringRecord("NAME_TXT", m_io, this, m_root);
                _descTxt = new StringRecord("DESC_TXT", m_io, this, m_root);
                _owner = new StringRecord("OWNER", m_io, this, m_root);
                _subrace = new StringRecord("SUBRACE", m_io, this, m_root);
                _stack = new StringRecord("STACK", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _group = new Group(m_io, this, m_root);
            }
            private StringRecord _cityId;
            private StringRecord _nameTxt;
            private StringRecord _descTxt;
            private StringRecord _owner;
            private StringRecord _subrace;
            private StringRecord _stack;
            private Position _position;
            private Group _group;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public StringRecord CityId { get { return _cityId; } }
            public StringRecord NameTxt { get { return _nameTxt; } }
            public StringRecord DescTxt { get { return _descTxt; } }
            public StringRecord Owner { get { return _owner; } }
            public StringRecord Subrace { get { return _subrace; } }
            public StringRecord Stack { get { return _stack; } }
            public Position Position { get { return _position; } }
            public Group Group { get { return _group; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// Represents single currency type such as gold or mana
        /// </summary>
        public partial class Currency : KaitaiStruct
        {
            public static Currency FromFile(string fileName)
            {
                return new Currency(new KaitaiStream(fileName));
            }

            public Currency(KaitaiStream p__io, Sg.BankRecord p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                f_amount = false;
                _read();
            }
            private void _read()
            {
                _type = m_io.ReadU1();
                _amountStr = Cp1251.Encoding.GetString(m_io.ReadBytes(4));
            }
            private bool f_amount;
            private int _amount;
            public int Amount
            {
                get
                {
                    if (f_amount)
                        return _amount;
                    f_amount = true;
                    _amount = (int) (Convert.ToInt64(AmountStr, 10));
                    return _amount;
                }
            }
            private byte _type;
            private string _amountStr;
            private Sg m_root;
            private Sg.BankRecord m_parent;
            public byte Type { get { return _type; } }
            public string AmountStr { get { return _amountStr; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.BankRecord M_Parent { get { return m_parent; } }
        }
        public partial class D2eeHeader : KaitaiStruct
        {
            public static D2eeHeader FromFile(string fileName)
            {
                return new D2eeHeader(new KaitaiStream(fileName));
            }

            public D2eeHeader(KaitaiStream p__io, Sg.SgHeader p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _zeroes = m_io.ReadUInt16();
                _ones = m_io.ReadUInt32();
                _size = m_io.ReadUInt32();
                _unknown1 = m_io.ReadBytes(4);
                if (!((KaitaiStream.ByteArrayCompare(_unknown1, new byte[] { 1, 0, 0, 0 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 1, 0, 0, 0 }, _unknown1, m_io, "/types/d2ee_header/seq/3");
                }
                _unknown2 = m_io.ReadBytes(4);
                if (!((KaitaiStream.ByteArrayCompare(_unknown2, new byte[] { 35, 0, 0, 0 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 35, 0, 0, 0 }, _unknown2, m_io, "/types/d2ee_header/seq/4");
                }
                _unknown3 = m_io.ReadBytes(4);
                if (!((KaitaiStream.ByteArrayCompare(_unknown3, new byte[] { 0, 0, 0, 0 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 0, 0, 0, 0 }, _unknown3, m_io, "/types/d2ee_header/seq/5");
                }
                _scenarioId = Cp1251.Encoding.GetString(KaitaiStream.BytesTerminate(m_io.ReadBytes(11), 0, false));
                _scenarioDescription = Cp1251.Encoding.GetString(KaitaiStream.BytesTerminate(m_io.ReadBytes(256), 0, false));
                _designerName = Cp1251.Encoding.GetString(KaitaiStream.BytesTerminate(m_io.ReadBytes(21), 0, false));
                _officialMap = m_io.ReadU1();
                _questName = Cp1251.Encoding.GetString(KaitaiStream.BytesTerminate(m_io.ReadBytes(256), 0, false));
                _mapSize = m_io.ReadUInt32();
                _difficulty = m_io.ReadUInt32();
                _turnNumber = m_io.ReadUInt32();
                _unknown4 = m_io.ReadUInt32();
                _campaignId = Cp1251.Encoding.GetString(m_io.ReadBytes(11));
                _suggLvl = m_io.ReadUInt32();
                _unknown5 = m_io.ReadBytes(1);
                _defaultPlayerName = Cp1251.Encoding.GetString(KaitaiStream.BytesTerminate(m_io.ReadBytes(256), 0, false));
                _race = m_io.ReadUInt32();
                _unknown6 = m_io.ReadBytes(815);
                _unknownData = m_io.ReadUInt32();
                _prngValues = new List<uint>(SgRead.Capacity(250));
                for (var i = 0; i < 250; i++)
                {
                    _prngValues.Add(m_io.ReadUInt32());
                }
                _paddingSize = m_io.ReadUInt32();
                _totalRaces = m_io.ReadUInt32();
                _races = new List<RaceInfo>(SgRead.Capacity(TotalRaces));
                for (var i = 0; i < TotalRaces; i++)
                {
                    _races.Add(new RaceInfo(m_io, this, m_root));
                }
                _padding = new List<byte>(SgRead.Capacity(PaddingSize));
                for (var i = 0; i < PaddingSize; i++)
                {
                    _padding.Add(m_io.ReadU1());
                }
            }
            private ushort _zeroes;
            private uint _ones;
            private uint _size;
            private byte[] _unknown1;
            private byte[] _unknown2;
            private byte[] _unknown3;
            private string _scenarioId;
            private string _scenarioDescription;
            private string _designerName;
            private byte _officialMap;
            private string _questName;
            private uint _mapSize;
            private uint _difficulty;
            private uint _turnNumber;
            private uint _unknown4;
            private string _campaignId;
            private uint _suggLvl;
            private byte[] _unknown5;
            private string _defaultPlayerName;
            private uint _race;
            private byte[] _unknown6;
            private uint _unknownData;
            private List<uint> _prngValues;
            private uint _paddingSize;
            private uint _totalRaces;
            private List<RaceInfo> _races;
            private List<byte> _padding;
            private Sg m_root;
            private Sg.SgHeader m_parent;
            public ushort Zeroes { get { return _zeroes; } }
            public uint Ones { get { return _ones; } }

            /// <summary>
            /// Header size.
            /// Can not be greater than 2920 bytes, according to the game code
            /// </summary>
            public uint Size { get { return _size; } }

            /// <summary>
            /// Must be 1 according to the game code
            /// </summary>
            public byte[] Unknown1 { get { return _unknown1; } }

            /// <summary>
            /// Must be 0x23 (35) according to the game code
            /// </summary>
            public byte[] Unknown2 { get { return _unknown2; } }
            public byte[] Unknown3 { get { return _unknown3; } }
            public string ScenarioId { get { return _scenarioId; } }
            public string ScenarioDescription { get { return _scenarioDescription; } }
            public string DesignerName { get { return _designerName; } }

            /// <summary>
            /// 1 means this map is made by 'Strategy First', 0 - 'Third Party'
            /// ScenarioEditor always set this to 0 when entering objectives menu
            /// </summary>
            public byte OfficialMap { get { return _officialMap; } }
            public string QuestName { get { return _questName; } }

            /// <summary>
            /// This value is shown in preview while selecting scenario to load
            /// </summary>
            public uint MapSize { get { return _mapSize; } }

            /// <summary>
            /// Difficulty value, same as ID in Ldiff.dbf
            /// </summary>
            public uint Difficulty { get { return _difficulty; } }
            public uint TurnNumber { get { return _turnNumber; } }
            public uint Unknown4 { get { return _unknown4; } }
            public string CampaignId { get { return _campaignId; } }

            /// <summary>
            /// Suggested leader level. Scenario Editor set it to the same value as in 'scenario_info.sugg_lvl'
            /// </summary>
            public uint SuggLvl { get { return _suggLvl; } }
            public byte[] Unknown5 { get { return _unknown5; } }

            /// <summary>
            /// Game allows to enter only 15 characters upon starting a scenario
            /// </summary>
            public string DefaultPlayerName { get { return _defaultPlayerName; } }

            /// <summary>
            /// Race value, same as ID in Lrace.dbf
            /// </summary>
            public uint Race { get { return _race; } }
            public byte[] Unknown6 { get { return _unknown6; } }
            public uint UnknownData { get { return _unknownData; } }

            /// <summary>
            /// Values used for pseudo-random number generation
            /// </summary>
            public List<uint> PrngValues { get { return _prngValues; } }
            public uint PaddingSize { get { return _paddingSize; } }

            /// <summary>
            /// Total races in scenario, including neutrals.
            /// This information is shown in scenario preview.
            /// </summary>
            public uint TotalRaces { get { return _totalRaces; } }
            public List<RaceInfo> Races { get { return _races; } }
            public List<byte> Padding { get { return _padding; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.SgHeader M_Parent { get { return m_parent; } }
        }
        public partial class DiplomacyEntry : KaitaiStruct
        {
            public static DiplomacyEntry FromFile(string fileName)
            {
                return new DiplomacyEntry(new KaitaiStream(fileName));
            }

            public DiplomacyEntry(KaitaiStream p__io, Sg.MidDiplomacy p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _race1 = new IntRecord("RACE_1", m_io, this, m_root);
                _race2 = new IntRecord("RACE_2", m_io, this, m_root);
                _relation = new IntRecord("RELATION", m_io, this, m_root);
            }
            private IntRecord _race1;
            private IntRecord _race2;
            private IntRecord _relation;
            private Sg m_root;
            private Sg.MidDiplomacy m_parent;
            public IntRecord Race1 { get { return _race1; } }
            public IntRecord Race2 { get { return _race2; } }
            public IntRecord Relation { get { return _relation; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidDiplomacy M_Parent { get { return m_parent; } }
        }
        public partial class DoubleId : KaitaiStruct
        {
            public static DoubleId FromFile(string fileName)
            {
                return new DoubleId(new KaitaiStream(fileName));
            }

            public DoubleId(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _id1 = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _id2 = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
            }
            private IntRecord _id1;
            private IntRecord _id2;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public IntRecord Id1 { get { return _id1; } }
            public IntRecord Id2 { get { return _id2; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class Endobject : KaitaiStruct
        {
            public static Endobject FromFile(string fileName)
            {
                return new Endobject(new KaitaiStream(fileName));
            }

            public Endobject(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _value = m_io.ReadBytes(10);
                if (!((KaitaiStream.ByteArrayCompare(_value, new byte[] { 69, 78, 68, 79, 66, 74, 69, 67, 84, 0 }) == 0)))
                {
                    throw new ValidationNotEqualError(new byte[] { 69, 78, 68, 79, 66, 74, 69, 67, 84, 0 }, _value, m_io, "/types/endobject/seq/0");
                }
            }
            private byte[] _value;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public byte[] Value { get { return _value; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class EventCondition : KaitaiStruct
        {
            public static EventCondition FromFile(string fileName)
            {
                return new EventCondition(new KaitaiStream(fileName));
            }

            public EventCondition(KaitaiStream p__io, Sg.MidEvent p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _category = new IntRecord("CATEGORY", m_io, this, m_root);
                switch (Category.Value) {
                case 0: {
                    _data = new EventConditionFrequency(m_io, this, m_root);
                    break;
                }
                case 10: {
                    _data = new EventConditionLootingARuin(m_io, this, m_root);
                    break;
                }
                case 11: {
                    _data = new EventConditionTransformingLand(m_io, this, m_root);
                    break;
                }
                case 12: {
                    _data = new EventConditionVisitingASite(m_io, this, m_root);
                    break;
                }
                case 14: {
                    _data = new EventConditionStackToLocation(m_io, this, m_root);
                    break;
                }
                case 15: {
                    _data = new EventConditionStackToCity(m_io, this, m_root);
                    break;
                }
                case 16: {
                    _data = new EventConditionItemToLocation(m_io, this, m_root);
                    break;
                }
                case 17: {
                    _data = new EventConditionStackExistance(m_io, this, m_root);
                    break;
                }
                case 18: {
                    _data = new EventConditionVariableIsInRange(m_io, this, m_root);
                    break;
                }
                case 2: {
                    _data = new EventConditionEnteringAPredefinedZone(m_io, this, m_root);
                    break;
                }
                case 3: {
                    _data = new EventConditionEnteringACity(m_io, this, m_root);
                    break;
                }
                case 4: {
                    _data = new EventConditionOwningACity(m_io, this, m_root);
                    break;
                }
                case 5: {
                    _data = new EventConditionDestroyStack(m_io, this, m_root);
                    break;
                }
                case 6: {
                    _data = new EventConditionOwningAnItem(m_io, this, m_root);
                    break;
                }
                case 7: {
                    _data = new EventConditionSpecificLeaderOwningAnItem(m_io, this, m_root);
                    break;
                }
                case 8: {
                    _data = new EventConditionDiplomacyRelations(m_io, this, m_root);
                    break;
                }
                case 9: {
                    _data = new EventConditionAlliance(m_io, this, m_root);
                    break;
                }
                }
            }
            private IntRecord _category;
            private KaitaiStruct _data;
            private Sg m_root;
            private Sg.MidEvent m_parent;
            public IntRecord Category { get { return _category; } }
            public KaitaiStruct Data { get { return _data; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidEvent M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// &lt;id player 1&gt; must be in alliance with &lt;id player 2&gt;
        /// </summary>
        public partial class EventConditionAlliance : KaitaiStruct
        {
            public static EventConditionAlliance FromFile(string fileName)
            {
                return new EventConditionAlliance(new KaitaiStream(fileName));
            }

            public EventConditionAlliance(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
                _idPlayer2 = new StringRecord("ID_PLAYER2", m_io, this, m_root);
            }
            private StringRecord _idPlayer1;
            private StringRecord _idPlayer2;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public StringRecord IdPlayer2 { get { return _idPlayer2; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// &lt;id stack&gt; must be killed by any of the affected races
        /// </summary>
        public partial class EventConditionDestroyStack : KaitaiStruct
        {
            public static EventConditionDestroyStack FromFile(string fileName)
            {
                return new EventConditionDestroyStack(new KaitaiStream(fileName));
            }

            public EventConditionDestroyStack(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
            }
            private StringRecord _idStack;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdStack { get { return _idStack; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// &lt;id player 1&gt; must be at &lt;diplomacy&gt; level with &lt;id player 2&gt;
        /// </summary>
        public partial class EventConditionDiplomacyRelations : KaitaiStruct
        {
            public static EventConditionDiplomacyRelations FromFile(string fileName)
            {
                return new EventConditionDiplomacyRelations(new KaitaiStream(fileName));
            }

            public EventConditionDiplomacyRelations(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
                _idPlayer2 = new StringRecord("ID_PLAYER2", m_io, this, m_root);
                _diplomacy = new IntRecord("DIPLOMACY", m_io, this, m_root);
            }
            private StringRecord _idPlayer1;
            private StringRecord _idPlayer2;
            private IntRecord _diplomacy;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public StringRecord IdPlayer2 { get { return _idPlayer2; } }
            public IntRecord Diplomacy { get { return _diplomacy; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// there is a stack from the affected races inside the specified city
        /// </summary>
        public partial class EventConditionEnteringACity : KaitaiStruct
        {
            public static EventConditionEnteringACity FromFile(string fileName)
            {
                return new EventConditionEnteringACity(new KaitaiStream(fileName));
            }

            public EventConditionEnteringACity(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idCity = new StringRecord("ID_CITY", m_io, this, m_root);
            }
            private StringRecord _idCity;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdCity { get { return _idCity; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// a stack from the affected races is inside the specified location
        /// </summary>
        public partial class EventConditionEnteringAPredefinedZone : KaitaiStruct
        {
            public static EventConditionEnteringAPredefinedZone FromFile(string fileName)
            {
                return new EventConditionEnteringAPredefinedZone(new KaitaiStream(fileName));
            }

            public EventConditionEnteringAPredefinedZone(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
            }
            private StringRecord _idLoc;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdLoc { get { return _idLoc; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// current turn must be a factor of the specified value
        /// </summary>
        public partial class EventConditionFrequency : KaitaiStruct
        {
            public static EventConditionFrequency FromFile(string fileName)
            {
                return new EventConditionFrequency(new KaitaiStream(fileName));
            }

            public EventConditionFrequency(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _frequency = new IntRecord("FREQUENCY", m_io, this, m_root);
            }
            private IntRecord _frequency;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public IntRecord Frequency { get { return _frequency; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// a stack from the affected races holding an item of the specified type must be inside the specified location
        /// </summary>
        public partial class EventConditionItemToLocation : KaitaiStruct
        {
            public static EventConditionItemToLocation FromFile(string fileName)
            {
                return new EventConditionItemToLocation(new KaitaiStream(fileName));
            }

            public EventConditionItemToLocation(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _typeItem = new StringRecord("TYPE_ITEM", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
            }
            private StringRecord _typeItem;
            private StringRecord _idLoc;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord TypeItem { get { return _typeItem; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// the specified ruin must be looted by any of the affected races
        /// </summary>
        public partial class EventConditionLootingARuin : KaitaiStruct
        {
            public static EventConditionLootingARuin FromFile(string fileName)
            {
                return new EventConditionLootingARuin(new KaitaiStream(fileName));
            }

            public EventConditionLootingARuin(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idRuin = new StringRecord("ID_RUIN", m_io, this, m_root);
            }
            private StringRecord _idRuin;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdRuin { get { return _idRuin; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// one of the affected races owns the specified city
        /// </summary>
        public partial class EventConditionOwningACity : KaitaiStruct
        {
            public static EventConditionOwningACity FromFile(string fileName)
            {
                return new EventConditionOwningACity(new KaitaiStream(fileName));
            }

            public EventConditionOwningACity(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idCity = new StringRecord("ID_CITY", m_io, this, m_root);
            }
            private StringRecord _idCity;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdCity { get { return _idCity; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// any of the affected races owns an item of the specified type
        /// </summary>
        public partial class EventConditionOwningAnItem : KaitaiStruct
        {
            public static EventConditionOwningAnItem FromFile(string fileName)
            {
                return new EventConditionOwningAnItem(new KaitaiStream(fileName));
            }

            public EventConditionOwningAnItem(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _typeItem = new StringRecord("TYPE_ITEM", m_io, this, m_root);
            }
            private StringRecord _typeItem;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord TypeItem { get { return _typeItem; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// specified stack must own specified item (only special items are listed)
        /// </summary>
        public partial class EventConditionSpecificLeaderOwningAnItem : KaitaiStruct
        {
            public static EventConditionSpecificLeaderOwningAnItem FromFile(string fileName)
            {
                return new EventConditionSpecificLeaderOwningAnItem(new KaitaiStream(fileName));
            }

            public EventConditionSpecificLeaderOwningAnItem(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _typeItem = new StringRecord("TYPE_ITEM", m_io, this, m_root);
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
            }
            private StringRecord _typeItem;
            private StringRecord _idStack;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord TypeItem { get { return _typeItem; } }
            public StringRecord IdStack { get { return _idStack; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// specified stack must exist or not
        /// </summary>
        public partial class EventConditionStackExistance : KaitaiStruct
        {
            public static EventConditionStackExistance FromFile(string fileName)
            {
                return new EventConditionStackExistance(new KaitaiStream(fileName));
            }

            public EventConditionStackExistance(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _miscInt = new IntRecord("MISC_INT", m_io, this, m_root);
            }
            private StringRecord _idStack;
            private IntRecord _miscInt;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdStack { get { return _idStack; } }

            /// <summary>
            /// 0 means stack must exist, 1 means stack must NOT exist
            /// </summary>
            public IntRecord MiscInt { get { return _miscInt; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// specified stack must be in specified city
        /// </summary>
        public partial class EventConditionStackToCity : KaitaiStruct
        {
            public static EventConditionStackToCity FromFile(string fileName)
            {
                return new EventConditionStackToCity(new KaitaiStream(fileName));
            }

            public EventConditionStackToCity(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _idCity = new StringRecord("ID_CITY", m_io, this, m_root);
            }
            private StringRecord _idStack;
            private StringRecord _idCity;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdStack { get { return _idStack; } }
            public StringRecord IdCity { get { return _idCity; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// specified stack must be on area covered by specified location
        /// </summary>
        public partial class EventConditionStackToLocation : KaitaiStruct
        {
            public static EventConditionStackToLocation FromFile(string fileName)
            {
                return new EventConditionStackToLocation(new KaitaiStream(fileName));
            }

            public EventConditionStackToLocation(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
            }
            private StringRecord _idStack;
            private StringRecord _idLoc;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdStack { get { return _idStack; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// one of the affected races must have transformed the specified percentage of the land
        /// </summary>
        public partial class EventConditionTransformingLand : KaitaiStruct
        {
            public static EventConditionTransformingLand FromFile(string fileName)
            {
                return new EventConditionTransformingLand(new KaitaiStream(fileName));
            }

            public EventConditionTransformingLand(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _pctLand = new IntRecord("PCT_LAND", m_io, this, m_root);
            }
            private IntRecord _pctLand;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public IntRecord PctLand { get { return _pctLand; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// the specified variable must be equal or between the min and max value specified
        /// </summary>
        public partial class EventConditionVariableIsInRange : KaitaiStruct
        {
            public static EventConditionVariableIsInRange FromFile(string fileName)
            {
                return new EventConditionVariableIsInRange(new KaitaiStream(fileName));
            }

            public EventConditionVariableIsInRange(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _miscInt = new IntRecord("MISC_INT", m_io, this, m_root);
                _miscInt2 = new IntRecord("MISC_INT2", m_io, this, m_root);
                _miscInt3 = new IntRecord("MISC_INT3", m_io, this, m_root);
                _miscInt4 = new IntRecord("MISC_INT4", m_io, this, m_root);
                _miscInt5 = new IntRecord("MISC_INT5", m_io, this, m_root);
                _miscInt6 = new IntRecord("MISC_INT6", m_io, this, m_root);
                _miscInt7 = new IntRecord("MISC_INT7", m_io, this, m_root);
            }
            private IntRecord _miscInt;
            private IntRecord _miscInt2;
            private IntRecord _miscInt3;
            private IntRecord _miscInt4;
            private IntRecord _miscInt5;
            private IntRecord _miscInt6;
            private IntRecord _miscInt7;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public IntRecord MiscInt { get { return _miscInt; } }
            public IntRecord MiscInt2 { get { return _miscInt2; } }
            public IntRecord MiscInt3 { get { return _miscInt3; } }
            public IntRecord MiscInt4 { get { return _miscInt4; } }
            public IntRecord MiscInt5 { get { return _miscInt5; } }
            public IntRecord MiscInt6 { get { return _miscInt6; } }
            public IntRecord MiscInt7 { get { return _miscInt7; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// a stack from the affected races must be inside the specified site
        /// </summary>
        public partial class EventConditionVisitingASite : KaitaiStruct
        {
            public static EventConditionVisitingASite FromFile(string fileName)
            {
                return new EventConditionVisitingASite(new KaitaiStream(fileName));
            }

            public EventConditionVisitingASite(KaitaiStream p__io, Sg.EventCondition p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idSite = new StringRecord("ID_SITE", m_io, this, m_root);
            }
            private StringRecord _idSite;
            private Sg m_root;
            private Sg.EventCondition m_parent;
            public StringRecord IdSite { get { return _idSite; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventCondition M_Parent { get { return m_parent; } }
        }
        public partial class EventEffect : KaitaiStruct
        {
            public static EventEffect FromFile(string fileName)
            {
                return new EventEffect(new KaitaiStream(fileName));
            }

            public EventEffect(KaitaiStream p__io, Sg.MidEvent p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _category = new IntRecord("CATEGORY", m_io, this, m_root);
                switch (Category.Value) {
                case 0: {
                    _data = new EventEffectWinOrLoseScenario(m_io, this, m_root);
                    break;
                }
                case 1: {
                    _data = new EventEffectCreateNewStack(m_io, this, m_root);
                    break;
                }
                case 10: {
                    _data = new EventEffectMoveStackToSpecificLocation(m_io, this, m_root);
                    break;
                }
                case 11: {
                    _data = new EventEffectAllyTwoAiPlayers(m_io, this, m_root);
                    break;
                }
                case 12: {
                    _data = new EventEffectChangePlayerDiplomacyMeter(m_io, this, m_root);
                    break;
                }
                case 13: {
                    _data = new EventEffectUnfogOrFogAnAreaOnTheMap(m_io, this, m_root);
                    break;
                }
                case 14: {
                    _data = new EventEffectRemoveMountainsAroundALocation(m_io, this, m_root);
                    break;
                }
                case 15: {
                    _data = new EventEffectRemoveLandmark(m_io, this, m_root);
                    break;
                }
                case 16: {
                    _data = new EventEffectChangeScenarioObjectiveText(m_io, this, m_root);
                    break;
                }
                case 17: {
                    _data = new EventEffectDisplayPopupMessage(m_io, this, m_root);
                    break;
                }
                case 18: {
                    _data = new EventEffectChangeStackOrder(m_io, this, m_root);
                    break;
                }
                case 19: {
                    _data = new EventEffectDestroyItem(m_io, this, m_root);
                    break;
                }
                case 2: {
                    _data = new EventEffectCastSpellOnTriggerer(m_io, this, m_root);
                    break;
                }
                case 20: {
                    _data = new EventEffectRemoveStack(m_io, this, m_root);
                    break;
                }
                case 21: {
                    _data = new EventEffectChangeLandmark(m_io, this, m_root);
                    break;
                }
                case 22: {
                    _data = new EventEffectChangeTerrain(m_io, this, m_root);
                    break;
                }
                case 23: {
                    _data = new EventEffectModifyVariable(m_io, this, m_root);
                    break;
                }
                case 3: {
                    _data = new EventEffectCastSpellAtSpecificLocation(m_io, this, m_root);
                    break;
                }
                case 4: {
                    _data = new EventEffectChangeStackOwner(m_io, this, m_root);
                    break;
                }
                case 5: {
                    _data = new EventEffectMoveStackNextToTriggerer(m_io, this, m_root);
                    break;
                }
                case 6: {
                    _data = new EventEffectGoIntoBattle(m_io, this, m_root);
                    break;
                }
                case 7: {
                    _data = new EventEffectEnableDisableAnotherEvent(m_io, this, m_root);
                    break;
                }
                case 8: {
                    _data = new EventEffectGiveSpell(m_io, this, m_root);
                    break;
                }
                case 9: {
                    _data = new EventEffectGiveItem(m_io, this, m_root);
                    break;
                }
                }
            }
            private IntRecord _category;
            private KaitaiStruct _data;
            private Sg m_root;
            private Sg.MidEvent m_parent;
            public IntRecord Category { get { return _category; } }
            public KaitaiStruct Data { get { return _data; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidEvent M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// ally the two specified players
        /// </summary>
        public partial class EventEffectAllyTwoAiPlayers : KaitaiStruct
        {
            public static EventEffectAllyTwoAiPlayers FromFile(string fileName)
            {
                return new EventEffectAllyTwoAiPlayers(new KaitaiStream(fileName));
            }

            public EventEffectAllyTwoAiPlayers(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
                _idPlayer2 = new StringRecord("ID_PLAYER2", m_io, this, m_root);
                if (M_Root.NewBody) {
                    _permally = new BoolRecord("PERMALLI", m_io, this, m_root);
                }
            }
            private IntRecord _num;
            private StringRecord _idPlayer1;
            private StringRecord _idPlayer2;
            private BoolRecord _permally;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public StringRecord IdPlayer2 { get { return _idPlayer2; } }

            /// <summary>
            /// disciples-godot — в MidFile версий 34 и 35 поля нет (главы AVALANCHE, «Путь Проклятых»). Маркер — PERMALLI, с опечаткой движка (CMidEvEffect L_ALLY, поток 0x61634a, тег 0x6f11a8); до 16.10 здесь стоял PERMALLY, и карта port-synth-3-events с таким маркером роняла Discipl2.exe при загрузке.
            /// </summary>
            public BoolRecord Permally { get { return _permally; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// cast the specified spell on the specified location
        /// </summary>
        public partial class EventEffectCastSpellAtSpecificLocation : KaitaiStruct
        {
            public static EventEffectCastSpellAtSpecificLocation FromFile(string fileName)
            {
                return new EventEffectCastSpellAtSpecificLocation(new KaitaiStream(fileName));
            }

            public EventEffectCastSpellAtSpecificLocation(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _typeSpell = new StringRecord("TYPE_SPELL", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _typeSpell;
            private StringRecord _idLoc;
            private StringRecord _idPlayer1;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord TypeSpell { get { return _typeSpell; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// cast the specified spell on the triggerer
        /// </summary>
        public partial class EventEffectCastSpellOnTriggerer : KaitaiStruct
        {
            public static EventEffectCastSpellOnTriggerer FromFile(string fileName)
            {
                return new EventEffectCastSpellOnTriggerer(new KaitaiStream(fileName));
            }

            public EventEffectCastSpellOnTriggerer(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _typeSpell = new StringRecord("TYPE_SPELL", m_io, this, m_root);
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _typeSpell;
            private StringRecord _idPlayer1;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord TypeSpell { get { return _typeSpell; } }

            /// <summary>
            /// id G000000000 means caster is triggerer
            /// </summary>
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// change the specified landmark to another type
        /// </summary>
        public partial class EventEffectChangeLandmark : KaitaiStruct
        {
            public static EventEffectChangeLandmark FromFile(string fileName)
            {
                return new EventEffectChangeLandmark(new KaitaiStream(fileName));
            }

            public EventEffectChangeLandmark(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idLmark = new StringRecord("ID_LMARK", m_io, this, m_root);
                _typeLmark = new StringRecord("TYPE_LMARK", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idLmark;
            private StringRecord _typeLmark;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdLmark { get { return _idLmark; } }
            public StringRecord TypeLmark { get { return _typeLmark; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// change the diplomacy relation between the two specified players
        /// </summary>
        public partial class EventEffectChangePlayerDiplomacyMeter : KaitaiStruct
        {
            public static EventEffectChangePlayerDiplomacyMeter FromFile(string fileName)
            {
                return new EventEffectChangePlayerDiplomacyMeter(new KaitaiStream(fileName));
            }

            public EventEffectChangePlayerDiplomacyMeter(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
                _idPlayer2 = new StringRecord("ID_PLAYER2", m_io, this, m_root);
                _diplomacy = new IntRecord("DIPLOMACY", m_io, this, m_root);
                if (M_Root.NewBody) {
                    _enable = new BoolRecord("ENABLE", m_io, this, m_root);
                }
            }
            private IntRecord _num;
            private StringRecord _idPlayer1;
            private StringRecord _idPlayer2;
            private IntRecord _diplomacy;
            private BoolRecord _enable;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public StringRecord IdPlayer2 { get { return _idPlayer2; } }
            public IntRecord Diplomacy { get { return _diplomacy; } }

            /// <summary>
            /// disciples-godot — в MidFile (версии 34 и 35) поля нет (главы AVALANCHE, «Путь Проклятых»).
            /// </summary>
            public BoolRecord Enable { get { return _enable; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// change the objective of the scenario
        /// </summary>
        public partial class EventEffectChangeScenarioObjectiveText : KaitaiStruct
        {
            public static EventEffectChangeScenarioObjectiveText FromFile(string fileName)
            {
                return new EventEffectChangeScenarioObjectiveText(new KaitaiStream(fileName));
            }

            public EventEffectChangeScenarioObjectiveText(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _objectTxt = new StringRecord("OBJECT_TXT", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _objectTxt;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord ObjectTxt { get { return _objectTxt; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// change the order of the specified stack
        /// </summary>
        public partial class EventEffectChangeStackOrder : KaitaiStruct
        {
            public static EventEffectChangeStackOrder FromFile(string fileName)
            {
                return new EventEffectChangeStackOrder(new KaitaiStream(fileName));
            }

            public EventEffectChangeStackOrder(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _orderTarg = new StringRecord("ORDER_TARG", m_io, this, m_root);
                _firstOnly = new BoolRecord("FIRST_ONLY", m_io, this, m_root);
                _order = new IntRecord("ORDER", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idStack;
            private StringRecord _orderTarg;
            private BoolRecord _firstOnly;
            private IntRecord _order;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdStack { get { return _idStack; } }
            public StringRecord OrderTarg { get { return _orderTarg; } }
            public BoolRecord FirstOnly { get { return _firstOnly; } }
            public IntRecord Order { get { return _order; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// change the owner of the specified stack
        /// </summary>
        public partial class EventEffectChangeStackOwner : KaitaiStruct
        {
            public static EventEffectChangeStackOwner FromFile(string fileName)
            {
                return new EventEffectChangeStackOwner(new KaitaiStream(fileName));
            }

            public EventEffectChangeStackOwner(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
                _firstOnly = new BoolRecord("FIRST_ONLY", m_io, this, m_root);
                _playAnim = new BoolRecord("PLAY_ANIM", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idStack;
            private StringRecord _idPlayer1;
            private BoolRecord _firstOnly;
            private BoolRecord _playAnim;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdStack { get { return _idStack; } }
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public BoolRecord FirstOnly { get { return _firstOnly; } }
            public BoolRecord PlayAnim { get { return _playAnim; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// change a block of terrain of size &lt;numvalue&gt; to &lt;lookup&gt; race terrain at location &lt;id_loc&gt;
        /// </summary>
        public partial class EventEffectChangeTerrain : KaitaiStruct
        {
            public static EventEffectChangeTerrain FromFile(string fileName)
            {
                return new EventEffectChangeTerrain(new KaitaiStream(fileName));
            }

            public EventEffectChangeTerrain(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
                _lookup = new IntRecord("LOOKUP", m_io, this, m_root);
                _numvalue = new IntRecord("NUMVALUE", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idLoc;
            private IntRecord _lookup;
            private IntRecord _numvalue;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public IntRecord Lookup { get { return _lookup; } }
            public IntRecord Numvalue { get { return _numvalue; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// create a stack from the specified template at the specified location
        /// </summary>
        public partial class EventEffectCreateNewStack : KaitaiStruct
        {
            public static EventEffectCreateNewStack FromFile(string fileName)
            {
                return new EventEffectCreateNewStack(new KaitaiStream(fileName));
            }

            public EventEffectCreateNewStack(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idStktemp = new StringRecord("ID_STKTEMP", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idStktemp;
            private StringRecord _idLoc;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdStktemp { get { return _idStktemp; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// destroy one or more item of the specified type
        /// </summary>
        public partial class EventEffectDestroyItem : KaitaiStruct
        {
            public static EventEffectDestroyItem FromFile(string fileName)
            {
                return new EventEffectDestroyItem(new KaitaiStream(fileName));
            }

            public EventEffectDestroyItem(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _typeItem = new StringRecord("TYPE_ITEM", m_io, this, m_root);
                _trigOnly = new BoolRecord("TRIG_ONLY", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _typeItem;
            private BoolRecord _trigOnly;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord TypeItem { get { return _typeItem; } }
            public BoolRecord TrigOnly { get { return _trigOnly; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// popup a message
        /// </summary>
        public partial class EventEffectDisplayPopupMessage : KaitaiStruct
        {
            public static EventEffectDisplayPopupMessage FromFile(string fileName)
            {
                return new EventEffectDisplayPopupMessage(new KaitaiStream(fileName));
            }

            public EventEffectDisplayPopupMessage(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _popupTxt = new StringRecord("POPUP_TXT", m_io, this, m_root);
                _music = new StringRecord("MUSIC", m_io, this, m_root);
                _sound = new StringRecord("SOUND", m_io, this, m_root);
                _image = new StringRecord("IMAGE", m_io, this, m_root);
                _image2 = new StringRecord("IMAGE2", m_io, this, m_root);
                _leftSide = new BoolRecord("LEFT_SIDE", m_io, this, m_root);
                _popupShow = new StringRecord("POPUP_SHOW", m_io, this, m_root);
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _boolvalue = new BoolRecord("BOOLVALUE", m_io, this, m_root);
                }
            }
            private IntRecord _num;
            private StringRecord _popupTxt;
            private StringRecord _music;
            private StringRecord _sound;
            private StringRecord _image;
            private StringRecord _image2;
            private BoolRecord _leftSide;
            private StringRecord _popupShow;
            private BoolRecord _boolvalue;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord PopupTxt { get { return _popupTxt; } }
            public StringRecord Music { get { return _music; } }
            public StringRecord Sound { get { return _sound; } }
            public StringRecord Image { get { return _image; } }
            public StringRecord Image2 { get { return _image2; } }
            public BoolRecord LeftSide { get { return _leftSide; } }
            public StringRecord PopupShow { get { return _popupShow; } }
            public BoolRecord Boolvalue { get { return _boolvalue; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// enable/disable another event
        /// </summary>
        public partial class EventEffectEnableDisableAnotherEvent : KaitaiStruct
        {
            public static EventEffectEnableDisableAnotherEvent FromFile(string fileName)
            {
                return new EventEffectEnableDisableAnotherEvent(new KaitaiStream(fileName));
            }

            public EventEffectEnableDisableAnotherEvent(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idEvent = new StringRecord("ID_EVENT", m_io, this, m_root);
                _enable = new BoolRecord("ENABLE", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idEvent;
            private BoolRecord _enable;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdEvent { get { return _idEvent; } }
            public BoolRecord Enable { get { return _enable; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// give the specified item to triggerer
        /// </summary>
        public partial class EventEffectGiveItem : KaitaiStruct
        {
            public static EventEffectGiveItem FromFile(string fileName)
            {
                return new EventEffectGiveItem(new KaitaiStream(fileName));
            }

            public EventEffectGiveItem(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _giveto = new IntRecord("GIVETO", m_io, this, m_root);
                _typeItem = new StringRecord("TYPE_ITEM", m_io, this, m_root);
            }
            private IntRecord _num;
            private IntRecord _giveto;
            private StringRecord _typeItem;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public IntRecord Giveto { get { return _giveto; } }
            public StringRecord TypeItem { get { return _typeItem; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// give the specified spell to the triggerer
        /// </summary>
        public partial class EventEffectGiveSpell : KaitaiStruct
        {
            public static EventEffectGiveSpell FromFile(string fileName)
            {
                return new EventEffectGiveSpell(new KaitaiStream(fileName));
            }

            public EventEffectGiveSpell(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _typeSpell = new StringRecord("TYPE_SPELL", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _typeSpell;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord TypeSpell { get { return _typeSpell; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// make the specified stack to go into battle with the triggerer
        /// </summary>
        public partial class EventEffectGoIntoBattle : KaitaiStruct
        {
            public static EventEffectGoIntoBattle FromFile(string fileName)
            {
                return new EventEffectGoIntoBattle(new KaitaiStream(fileName));
            }

            public EventEffectGoIntoBattle(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _firstOnly = new BoolRecord("FIRST_ONLY", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idStack;
            private BoolRecord _firstOnly;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdStack { get { return _idStack; } }
            public BoolRecord FirstOnly { get { return _firstOnly; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// modifies the value of a variable
        /// </summary>
        public partial class EventEffectModifyVariable : KaitaiStruct
        {
            public static EventEffectModifyVariable FromFile(string fileName)
            {
                return new EventEffectModifyVariable(new KaitaiStream(fileName));
            }

            public EventEffectModifyVariable(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _lookup = new IntRecord("LOOKUP", m_io, this, m_root);
                _numvalue = new IntRecord("NUMVALUE", m_io, this, m_root);
                _numvalue2 = new IntRecord("NUMVALUE2", m_io, this, m_root);
            }
            private IntRecord _num;
            private IntRecord _lookup;
            private IntRecord _numvalue;
            private IntRecord _numvalue2;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public IntRecord Lookup { get { return _lookup; } }
            public IntRecord Numvalue { get { return _numvalue; } }
            public IntRecord Numvalue2 { get { return _numvalue2; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// move the specified stack beside the triggerer
        /// </summary>
        public partial class EventEffectMoveStackNextToTriggerer : KaitaiStruct
        {
            public static EventEffectMoveStackNextToTriggerer FromFile(string fileName)
            {
                return new EventEffectMoveStackNextToTriggerer(new KaitaiStream(fileName));
            }

            public EventEffectMoveStackNextToTriggerer(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idStack;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdStack { get { return _idStack; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// move the specified stack over the specified location
        /// </summary>
        public partial class EventEffectMoveStackToSpecificLocation : KaitaiStruct
        {
            public static EventEffectMoveStackToSpecificLocation FromFile(string fileName)
            {
                return new EventEffectMoveStackToSpecificLocation(new KaitaiStream(fileName));
            }

            public EventEffectMoveStackToSpecificLocation(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idStktemp = new StringRecord("ID_STKTEMP", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _boolvalue = new BoolRecord("BOOLVALUE", m_io, this, m_root);
                }
            }
            private IntRecord _num;
            private StringRecord _idStktemp;
            private StringRecord _idLoc;
            private BoolRecord _boolvalue;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdStktemp { get { return _idStktemp; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public BoolRecord Boolvalue { get { return _boolvalue; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// remove the specified landmark
        /// </summary>
        public partial class EventEffectRemoveLandmark : KaitaiStruct
        {
            public static EventEffectRemoveLandmark FromFile(string fileName)
            {
                return new EventEffectRemoveLandmark(new KaitaiStream(fileName));
            }

            public EventEffectRemoveLandmark(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idLmark = new StringRecord("ID_LMARK", m_io, this, m_root);
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _boolvalue = new BoolRecord("BOOLVALUE", m_io, this, m_root);
                }
            }
            private IntRecord _num;
            private StringRecord _idLmark;
            private BoolRecord _boolvalue;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdLmark { get { return _idLmark; } }
            public BoolRecord Boolvalue { get { return _boolvalue; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// remove mountains over the specified location
        /// </summary>
        public partial class EventEffectRemoveMountainsAroundALocation : KaitaiStruct
        {
            public static EventEffectRemoveMountainsAroundALocation FromFile(string fileName)
            {
                return new EventEffectRemoveMountainsAroundALocation(new KaitaiStream(fileName));
            }

            public EventEffectRemoveMountainsAroundALocation(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idLoc;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// remove the specified stack from map
        /// </summary>
        public partial class EventEffectRemoveStack : KaitaiStruct
        {
            public static EventEffectRemoveStack FromFile(string fileName)
            {
                return new EventEffectRemoveStack(new KaitaiStream(fileName));
            }

            public EventEffectRemoveStack(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _firstOnly = new BoolRecord("FIRST_ONLY", m_io, this, m_root);
            }
            private IntRecord _num;
            private StringRecord _idStack;
            private BoolRecord _firstOnly;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdStack { get { return _idStack; } }
            public BoolRecord FirstOnly { get { return _firstOnly; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// unfog or fog an area of the map over the specified location for the specified players
        /// </summary>
        public partial class EventEffectUnfogOrFogAnAreaOnTheMap : KaitaiStruct
        {
            public static EventEffectUnfogOrFogAnAreaOnTheMap FromFile(string fileName)
            {
                return new EventEffectUnfogOrFogAnAreaOnTheMap(new KaitaiStream(fileName));
            }

            public EventEffectUnfogOrFogAnAreaOnTheMap(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _idLoc = new StringRecord("ID_LOC", m_io, this, m_root);
                _eventCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _events = new List<EventEffectUnfogOrFogAnAreaOnTheMapEntry>(SgRead.Capacity(EventCount.Value));
                for (var i = 0; i < EventCount.Value; i++)
                {
                    _events.Add(new EventEffectUnfogOrFogAnAreaOnTheMapEntry(m_io, this, m_root));
                }
                if (M_Root.NewBody) {
                    _enable = new BoolRecord("ENABLE", m_io, this, m_root);
                }
                if (M_Root.NewBody) {
                    _numvalue = new IntRecord("NUMVALUE", m_io, this, m_root);
                }
            }
            private IntRecord _num;
            private StringRecord _idLoc;
            private IntRecord _eventCount;
            private List<EventEffectUnfogOrFogAnAreaOnTheMapEntry> _events;
            private BoolRecord _enable;
            private IntRecord _numvalue;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public StringRecord IdLoc { get { return _idLoc; } }
            public IntRecord EventCount { get { return _eventCount; } }
            public List<EventEffectUnfogOrFogAnAreaOnTheMapEntry> Events { get { return _events; } }
            public BoolRecord Enable { get { return _enable; } }
            public IntRecord Numvalue { get { return _numvalue; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }
        public partial class EventEffectUnfogOrFogAnAreaOnTheMapEntry : KaitaiStruct
        {
            public static EventEffectUnfogOrFogAnAreaOnTheMapEntry FromFile(string fileName)
            {
                return new EventEffectUnfogOrFogAnAreaOnTheMapEntry(new KaitaiStream(fileName));
            }

            public EventEffectUnfogOrFogAnAreaOnTheMapEntry(KaitaiStream p__io, Sg.EventEffectUnfogOrFogAnAreaOnTheMap p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _eventId = new StringRecord("EVENT_ID", m_io, this, m_root);
                _player = new StringRecord("PLAYER", m_io, this, m_root);
            }
            private StringRecord _eventId;
            private StringRecord _player;
            private Sg m_root;
            private Sg.EventEffectUnfogOrFogAnAreaOnTheMap m_parent;
            public StringRecord EventId { get { return _eventId; } }
            public StringRecord Player { get { return _player; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffectUnfogOrFogAnAreaOnTheMap M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// win or lose the scenario
        /// </summary>
        public partial class EventEffectWinOrLoseScenario : KaitaiStruct
        {
            public static EventEffectWinOrLoseScenario FromFile(string fileName)
            {
                return new EventEffectWinOrLoseScenario(new KaitaiStream(fileName));
            }

            public EventEffectWinOrLoseScenario(KaitaiStream p__io, Sg.EventEffect p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _num = new IntRecord("NUM", m_io, this, m_root);
                _winScen = new BoolRecord("WIN_SCEN", m_io, this, m_root);
                _idPlayer1 = new StringRecord("ID_PLAYER1", m_io, this, m_root);
            }
            private IntRecord _num;
            private BoolRecord _winScen;
            private StringRecord _idPlayer1;
            private Sg m_root;
            private Sg.EventEffect m_parent;
            public IntRecord Num { get { return _num; } }
            public BoolRecord WinScen { get { return _winScen; } }
            public StringRecord IdPlayer1 { get { return _idPlayer1; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.EventEffect M_Parent { get { return m_parent; } }
        }
        public partial class FogEntry : KaitaiStruct
        {
            public static FogEntry FromFile(string fileName)
            {
                return new FogEntry(new KaitaiStream(fileName));
            }

            public FogEntry(KaitaiStream p__io, Sg.MidgardMapFog p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _posY = new IntRecord("POS_Y", m_io, this, m_root);
                _fog = new IntRecord("FOG", m_io, this, m_root);
                _fogMask = SgRead.BitsBe(m_io, Fog.Value * 8);
            }
            private IntRecord _posY;
            private IntRecord _fog;
            private List<bool> _fogMask;
            private Sg m_root;
            private Sg.MidgardMapFog m_parent;
            public IntRecord PosY { get { return _posY; } }
            public IntRecord Fog { get { return _fog; } }

            /// <summary>
            /// For each Y position we have an entry with a bitmask
            /// where each bit corresponds to X position,
            /// thus defining a fog for a tile.
            /// </summary>
            public List<bool> FogMask { get { return _fogMask; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidgardMapFog M_Parent { get { return m_parent; } }
        }
        public partial class Group : KaitaiStruct
        {
            public static Group FromFile(string fileName)
            {
                return new Group(new KaitaiStream(fileName));
            }

            public Group(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _groupId = new StringRecord("GROUP_ID", m_io, this, m_root);
                _units = new List<StringRecord>(SgRead.Capacity(6));
                for (var i = 0; i < 6; i++)
                {
                    _units.Add(new StringRecord("UNIT_N", m_io, this, m_root));
                }
                _positions = new List<IntRecord>(SgRead.Capacity(6));
                for (var i = 0; i < 6; i++)
                {
                    _positions.Add(new IntRecord("POS_N", m_io, this, m_root));
                }
            }
            private StringRecord _groupId;
            private List<StringRecord> _units;
            private List<IntRecord> _positions;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public StringRecord GroupId { get { return _groupId; } }
            public List<StringRecord> Units { get { return _units; } }
            public List<IntRecord> Positions { get { return _positions; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class IntRecord : KaitaiStruct
        {
            public IntRecord(string p_nameValue, KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _nameValue = p_nameValue;
                _read();
            }
            private void _read()
            {
                _name = SgRead.Name(m_io, NameValue);
                _value = m_io.ReadUInt32();
            }
            private string _name;
            private uint _value;
            private string _nameValue;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public string Name { get { return _name; } }
            public uint Value { get { return _value; } }
            public string NameValue { get { return _nameValue; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class MerchantItemEntry : KaitaiStruct
        {
            public static MerchantItemEntry FromFile(string fileName)
            {
                return new MerchantItemEntry(new KaitaiStream(fileName));
            }

            public MerchantItemEntry(KaitaiStream p__io, Sg.MidSiteMerchant p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _itemId = new StringRecord("ITEM_ID", m_io, this, m_root);
                _itemCount = new IntRecord("ITEM_COUNT", m_io, this, m_root);
            }
            private StringRecord _itemId;
            private IntRecord _itemCount;
            private Sg m_root;
            private Sg.MidSiteMerchant m_parent;
            public StringRecord ItemId { get { return _itemId; } }
            public IntRecord ItemCount { get { return _itemCount; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidSiteMerchant M_Parent { get { return m_parent; } }
        }
        public partial class MercsUnitEntry : KaitaiStruct
        {
            public static MercsUnitEntry FromFile(string fileName)
            {
                return new MercsUnitEntry(new KaitaiStream(fileName));
            }

            public MercsUnitEntry(KaitaiStream p__io, Sg.MidSiteMercs p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _unitId = new StringRecord("UNIT_ID", m_io, this, m_root);
                _unitLevel = new IntRecord("UNIT_LEVEL", m_io, this, m_root);
                _unitUniq = new BoolRecord("UNIT_UNIQ", m_io, this, m_root);
            }
            private StringRecord _unitId;
            private IntRecord _unitLevel;
            private BoolRecord _unitUniq;
            private Sg m_root;
            private Sg.MidSiteMercs m_parent;
            public StringRecord UnitId { get { return _unitId; } }
            public IntRecord UnitLevel { get { return _unitLevel; } }
            public BoolRecord UnitUniq { get { return _unitUniq; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidSiteMercs M_Parent { get { return m_parent; } }
        }
        public partial class MidBag : KaitaiStruct
        {
            public static MidBag FromFile(string fileName)
            {
                return new MidBag(new KaitaiStream(fileName));
            }

            public MidBag(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _bagId = new StringRecord("BAG_ID", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _image = new IntRecord("IMAGE", m_io, this, m_root);
                _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
                _itemCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _items = new List<StringRecord>(SgRead.Capacity(ItemCount.Value));
                for (var i = 0; i < ItemCount.Value; i++)
                {
                    _items.Add(new StringRecord("ITEM_ID", m_io, this, m_root));
                }
            }
            private StringRecord _bagId;
            private Position _position;
            private IntRecord _image;
            private IntRecord _aipriority;
            private IntRecord _itemCount;
            private List<StringRecord> _items;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord BagId { get { return _bagId; } }
            public Position Position { get { return _position; } }
            public IntRecord Image { get { return _image; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public IntRecord ItemCount { get { return _itemCount; } }
            public List<StringRecord> Items { get { return _items; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidCrystal : KaitaiStruct
        {
            public static MidCrystal FromFile(string fileName)
            {
                return new MidCrystal(new KaitaiStream(fileName));
            }

            public MidCrystal(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _crystalId = new StringRecord("CRYSTAL_ID", m_io, this, m_root);
                _reource = new IntRecord("RESOURCE", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
            }
            private StringRecord _crystalId;
            private IntRecord _reource;
            private Position _position;
            private IntRecord _aipriority;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord CrystalId { get { return _crystalId; } }
            public IntRecord Reource { get { return _reource; } }
            public Position Position { get { return _position; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidDiplomacy : KaitaiStruct
        {
            public static MidDiplomacy FromFile(string fileName)
            {
                return new MidDiplomacy(new KaitaiStream(fileName));
            }

            public MidDiplomacy(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _count = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _entries = new List<DiplomacyEntry>(SgRead.Capacity(Count.Value));
                for (var i = 0; i < Count.Value; i++)
                {
                    _entries.Add(new DiplomacyEntry(m_io, this, m_root));
                }
            }
            private IntRecord _count;
            private List<DiplomacyEntry> _entries;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord Count { get { return _count; } }
            public List<DiplomacyEntry> Entries { get { return _entries; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidEvent : KaitaiStruct
        {
            public static MidEvent FromFile(string fileName)
            {
                return new MidEvent(new KaitaiStream(fileName));
            }

            public MidEvent(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _id = new StringRecord("ID", m_io, this, m_root);
                _nameTxt = new StringRecord("NAME_TXT", m_io, this, m_root);
                _human = new BoolRecord("HUMAN", m_io, this, m_root);
                _dwarf = new BoolRecord("DWARF", m_io, this, m_root);
                _undead = new BoolRecord("UNDEAD", m_io, this, m_root);
                _heretic = new BoolRecord("HERETIC", m_io, this, m_root);
                _neutral = new BoolRecord("NEUTRAL", m_io, this, m_root);
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _elf = new BoolRecord("ELF", m_io, this, m_root);
                }
                _verhuman = new BoolRecord("VERHUMAN", m_io, this, m_root);
                _verdwarf = new BoolRecord("VERDWARF", m_io, this, m_root);
                _verundead = new BoolRecord("VERUNDEAD", m_io, this, m_root);
                _verheretic = new BoolRecord("VERHERETIC", m_io, this, m_root);
                _verneutral = new BoolRecord("VERNEUTRAL", m_io, this, m_root);
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _verelf = new BoolRecord("VERELF", m_io, this, m_root);
                }
                _enabled = new BoolRecord("ENABLED", m_io, this, m_root);
                _occurOnce = new BoolRecord("OCCUR_ONCE", m_io, this, m_root);
                _chance = new IntRecord("CHANCE", m_io, this, m_root);
                _order = new IntRecord("ORDER", m_io, this, m_root);
                _condQty = new IntRecord("COND_QTY", m_io, this, m_root);
                _conditions = new List<EventCondition>(SgRead.Capacity(CondQty.Value));
                for (var i = 0; i < CondQty.Value; i++)
                {
                    _conditions.Add(new EventCondition(m_io, this, m_root));
                }
                _effectQty = new IntRecord("EFFECT_QTY", m_io, this, m_root);
                _effects = new List<EventEffect>(SgRead.Capacity(EffectQty.Value));
                for (var i = 0; i < EffectQty.Value; i++)
                {
                    _effects.Add(new EventEffect(m_io, this, m_root));
                }
            }
            private StringRecord _id;
            private StringRecord _nameTxt;
            private BoolRecord _human;
            private BoolRecord _dwarf;
            private BoolRecord _undead;
            private BoolRecord _heretic;
            private BoolRecord _neutral;
            private BoolRecord _elf;
            private BoolRecord _verhuman;
            private BoolRecord _verdwarf;
            private BoolRecord _verundead;
            private BoolRecord _verheretic;
            private BoolRecord _verneutral;
            private BoolRecord _verelf;
            private BoolRecord _enabled;
            private BoolRecord _occurOnce;
            private IntRecord _chance;
            private IntRecord _order;
            private IntRecord _condQty;
            private List<EventCondition> _conditions;
            private IntRecord _effectQty;
            private List<EventEffect> _effects;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord Id { get { return _id; } }
            public StringRecord NameTxt { get { return _nameTxt; } }
            public BoolRecord Human { get { return _human; } }
            public BoolRecord Dwarf { get { return _dwarf; } }
            public BoolRecord Undead { get { return _undead; } }
            public BoolRecord Heretic { get { return _heretic; } }
            public BoolRecord Neutral { get { return _neutral; } }
            public BoolRecord Elf { get { return _elf; } }
            public BoolRecord Verhuman { get { return _verhuman; } }
            public BoolRecord Verdwarf { get { return _verdwarf; } }
            public BoolRecord Verundead { get { return _verundead; } }
            public BoolRecord Verheretic { get { return _verheretic; } }
            public BoolRecord Verneutral { get { return _verneutral; } }
            public BoolRecord Verelf { get { return _verelf; } }
            public BoolRecord Enabled { get { return _enabled; } }
            public BoolRecord OccurOnce { get { return _occurOnce; } }
            public IntRecord Chance { get { return _chance; } }
            public IntRecord Order { get { return _order; } }

            /// <summary>
            /// number of event conditions
            /// </summary>
            public IntRecord CondQty { get { return _condQty; } }
            public List<EventCondition> Conditions { get { return _conditions; } }

            /// <summary>
            /// number of event effects
            /// </summary>
            public IntRecord EffectQty { get { return _effectQty; } }
            public List<EventEffect> Effects { get { return _effects; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidItem : KaitaiStruct
        {
            public static MidItem FromFile(string fileName)
            {
                return new MidItem(new KaitaiStream(fileName));
            }

            public MidItem(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _itemId = new StringRecord("ITEM_ID", m_io, this, m_root);
                _itemType = new StringRecord("ITEM_TYPE", m_io, this, m_root);
            }
            private StringRecord _itemId;
            private StringRecord _itemType;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord ItemId { get { return _itemId; } }
            public StringRecord ItemType { get { return _itemType; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidLandmark : KaitaiStruct
        {
            public static MidLandmark FromFile(string fileName)
            {
                return new MidLandmark(new KaitaiStream(fileName));
            }

            public MidLandmark(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _lmarkId = new StringRecord("LMARK_ID", m_io, this, m_root);
                _type = new StringRecord("TYPE", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                if (M_Root.NewBody) {
                    _descTxt = new StringRecord("DESC_TXT", m_io, this, m_root);
                }
            }
            private StringRecord _lmarkId;
            private StringRecord _type;
            private Position _position;
            private StringRecord _descTxt;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord LmarkId { get { return _lmarkId; } }
            public StringRecord Type { get { return _type; } }
            public Position Position { get { return _position; } }
            public StringRecord DescTxt { get { return _descTxt; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidLocation : KaitaiStruct
        {
            public static MidLocation FromFile(string fileName)
            {
                return new MidLocation(new KaitaiStream(fileName));
            }

            public MidLocation(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _locId = new StringRecord("LOC_ID", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _nameTxt = new StringRecord("NAME_TXT", m_io, this, m_root);
                _radius = new IntRecord("RADIUS", m_io, this, m_root);
            }
            private StringRecord _locId;
            private Position _position;
            private StringRecord _nameTxt;
            private IntRecord _radius;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord LocId { get { return _locId; } }
            public Position Position { get { return _position; } }
            public StringRecord NameTxt { get { return _nameTxt; } }
            public IntRecord Radius { get { return _radius; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidMountains : KaitaiStruct
        {
            public static MidMountains FromFile(string fileName)
            {
                return new MidMountains(new KaitaiStream(fileName));
            }

            public MidMountains(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _mountainsCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _mountains = new List<MountainsEntry>(SgRead.Capacity(MountainsCount.Value));
                for (var i = 0; i < MountainsCount.Value; i++)
                {
                    _mountains.Add(new MountainsEntry(m_io, this, m_root));
                }
            }
            private IntRecord _mountainsCount;
            private List<MountainsEntry> _mountains;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord MountainsCount { get { return _mountainsCount; } }
            public List<MountainsEntry> Mountains { get { return _mountains; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidPlayer : KaitaiStruct
        {
            public static MidPlayer FromFile(string fileName)
            {
                return new MidPlayer(new KaitaiStream(fileName));
            }

            public MidPlayer(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _playerId = new StringRecord("PLAYER_ID", m_io, this, m_root);
                _nameTxt = new StringRecord("NAME_TXT", m_io, this, m_root);
                _descTxt = new StringRecord("DESC_TXT", m_io, this, m_root);
                _lordId = new StringRecord("LORD_ID", m_io, this, m_root);
                _raceId = new StringRecord("RACE_ID", m_io, this, m_root);
                _fogId = new StringRecord("FOG_ID", m_io, this, m_root);
                _knownId = new StringRecord("KNOWN_ID", m_io, this, m_root);
                _buildsId = new StringRecord("BUILDS_ID", m_io, this, m_root);
                _face = new IntRecord("FACE", m_io, this, m_root);
                _qtyBreaks = new IntRecord("QTY_BREAKS", m_io, this, m_root);
                _bank = new BankRecord("BANK", m_io, this, m_root);
                _isHuman = new BoolRecord("IS_HUMAN", m_io, this, m_root);
                _spellBank = new BankRecord("SPELL_BANK", m_io, this, m_root);
                _attitude = new IntRecord("ATTITUDE", m_io, this, m_root);
                _researT = new IntRecord("RESEAR_T", m_io, this, m_root);
                _constrT = new IntRecord("CONSTR_T", m_io, this, m_root);
                _spies = new List<StringRecord>(SgRead.Capacity(3));
                for (var i = 0; i < 3; i++)
                {
                    _spies.Add(new StringRecord("SPY_N", m_io, this, m_root));
                }
                _captBy = new StringRecord("CAPT_BY", m_io, this, m_root);
                if (M_Root.NewBody) {
                    _alwaysai = new BoolRecord("ALWAYSAI", m_io, this, m_root);
                }
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _exmapRecords = new List<MidPlayerExmapRecord>(SgRead.Capacity(3));
                    for (var i = 0; i < 3; i++)
                    {
                        _exmapRecords.Add(new MidPlayerExmapRecord(m_io, this, m_root));
                    }
                }
            }
            private StringRecord _playerId;
            private StringRecord _nameTxt;
            private StringRecord _descTxt;
            private StringRecord _lordId;
            private StringRecord _raceId;
            private StringRecord _fogId;
            private StringRecord _knownId;
            private StringRecord _buildsId;
            private IntRecord _face;
            private IntRecord _qtyBreaks;
            private BankRecord _bank;
            private BoolRecord _isHuman;
            private BankRecord _spellBank;
            private IntRecord _attitude;
            private IntRecord _researT;
            private IntRecord _constrT;
            private List<StringRecord> _spies;
            private StringRecord _captBy;
            private BoolRecord _alwaysai;
            private List<MidPlayerExmapRecord> _exmapRecords;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord PlayerId { get { return _playerId; } }
            public StringRecord NameTxt { get { return _nameTxt; } }
            public StringRecord DescTxt { get { return _descTxt; } }
            public StringRecord LordId { get { return _lordId; } }
            public StringRecord RaceId { get { return _raceId; } }
            public StringRecord FogId { get { return _fogId; } }
            public StringRecord KnownId { get { return _knownId; } }
            public StringRecord BuildsId { get { return _buildsId; } }
            public IntRecord Face { get { return _face; } }
            public IntRecord QtyBreaks { get { return _qtyBreaks; } }
            public BankRecord Bank { get { return _bank; } }
            public BoolRecord IsHuman { get { return _isHuman; } }
            public BankRecord SpellBank { get { return _spellBank; } }
            public IntRecord Attitude { get { return _attitude; } }
            public IntRecord ResearT { get { return _researT; } }
            public IntRecord ConstrT { get { return _constrT; } }
            public List<StringRecord> Spies { get { return _spies; } }
            public StringRecord CaptBy { get { return _captBy; } }
            public BoolRecord Alwaysai { get { return _alwaysai; } }
            public List<MidPlayerExmapRecord> ExmapRecords { get { return _exmapRecords; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidPlayerExmapRecord : KaitaiStruct
        {
            public static MidPlayerExmapRecord FromFile(string fileName)
            {
                return new MidPlayerExmapRecord(new KaitaiStream(fileName));
            }

            public MidPlayerExmapRecord(KaitaiStream p__io, Sg.MidPlayer p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _id = new StringRecord("EXMAPIDx", m_io, this, m_root);
                _turn = new IntRecord("EXMAPTURNx", m_io, this, m_root);
            }
            private StringRecord _id;
            private IntRecord _turn;
            private Sg m_root;
            private Sg.MidPlayer m_parent;
            public StringRecord Id { get { return _id; } }

            /// <summary>
            /// Число, а не строка. Пока все три записи пусты (G000000000, ход 0), чтение строкой
            /// сходилось случайно: длина 0. Первый обмен картами (20260911-passive, ход 15:
            /// EXMAPID1 = S073PL0009, EXMAPTURN1 = 15) сдвигал разбор на 15 байт и ронял весь файл.
            /// </summary>
            public IntRecord Turn { get { return _turn; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidPlayer M_Parent { get { return m_parent; } }
        }
        public partial class MidQuestLog : KaitaiStruct
        {
            public static MidQuestLog FromFile(string fileName)
            {
                return new MidQuestLog(new KaitaiStream(fileName));
            }

            public MidQuestLog(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _logCount = new IntRecord("XxxxXXxxxx", m_io, this, m_root);
                _sequences = new List<MidQuestLogSequence>(SgRead.Capacity(LogCount.Value));
                for (var i = 0; i < LogCount.Value; i++)
                {
                    _sequences.Add(new MidQuestLogSequence(m_io, this, m_root));
                }
            }
            private IntRecord _logCount;
            private List<MidQuestLogSequence> _sequences;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord LogCount { get { return _logCount; } }
            public List<MidQuestLogSequence> Sequences { get { return _sequences; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidQuestLogSequence : KaitaiStruct
        {
            public static MidQuestLogSequence FromFile(string fileName)
            {
                return new MidQuestLogSequence(new KaitaiStream(fileName));
            }

            public MidQuestLogSequence(KaitaiStream p__io, Sg.MidQuestLog p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idPlayer = new StringRecord("ID_PLAYER", m_io, this, m_root);
                _seqNum = new IntRecord("SEQ_NUM", m_io, this, m_root);
                _curTurn = new IntRecord("CUR_TURN", m_io, this, m_root);
                _type = new IntRecord("TYPE", m_io, this, m_root);
                _idEvent = new StringRecord("ID_EVENT", m_io, this, m_root);
                _effNum = new IntRecord("EFF_NUM", m_io, this, m_root);
            }
            private StringRecord _idPlayer;
            private IntRecord _seqNum;
            private IntRecord _curTurn;
            private IntRecord _type;
            private StringRecord _idEvent;
            private IntRecord _effNum;
            private Sg m_root;
            private Sg.MidQuestLog m_parent;
            public StringRecord IdPlayer { get { return _idPlayer; } }
            public IntRecord SeqNum { get { return _seqNum; } }
            public IntRecord CurTurn { get { return _curTurn; } }
            public IntRecord Type { get { return _type; } }
            public StringRecord IdEvent { get { return _idEvent; } }
            public IntRecord EffNum { get { return _effNum; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidQuestLog M_Parent { get { return m_parent; } }
        }
        public partial class MidRoad : KaitaiStruct
        {
            public static MidRoad FromFile(string fileName)
            {
                return new MidRoad(new KaitaiStream(fileName));
            }

            public MidRoad(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _roadId = new StringRecord("ROAD_ID", m_io, this, m_root);
                _index = new IntRecord("INDEX", m_io, this, m_root);
                _var = new IntRecord("VAR", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
            }
            private StringRecord _roadId;
            private IntRecord _index;
            private IntRecord _var;
            private Position _position;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord RoadId { get { return _roadId; } }
            public IntRecord Index { get { return _index; } }
            public IntRecord Var { get { return _var; } }
            public Position Position { get { return _position; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidRod : KaitaiStruct
        {
            public static MidRod FromFile(string fileName)
            {
                return new MidRod(new KaitaiStream(fileName));
            }

            public MidRod(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _rodId = new StringRecord("ROD_ID", m_io, this, m_root);
                _owner = new StringRecord("OWNER", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
            }
            private StringRecord _rodId;
            private StringRecord _owner;
            private Position _position;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord RodId { get { return _rodId; } }
            public StringRecord Owner { get { return _owner; } }
            public Position Position { get { return _position; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidRuin : KaitaiStruct
        {
            public static MidRuin FromFile(string fileName)
            {
                return new MidRuin(new KaitaiStream(fileName));
            }

            public MidRuin(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _ruinId = new StringRecord("RUIN_ID", m_io, this, m_root);
                _title = new StringRecord("TITLE", m_io, this, m_root);
                _desc = new StringRecord("DESC", m_io, this, m_root);
                _image = new IntRecord("IMAGE", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _cash = new BankRecord("CASH", m_io, this, m_root);
                _item = new StringRecord("ITEM", m_io, this, m_root);
                _looter = new StringRecord("LOOTER", m_io, this, m_root);
                _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
                _visitorCount = new IntRecord("XxxxXXxxxx", m_io, this, m_root);
                _visitors = new List<RuinVisiter>(SgRead.Capacity(VisitorCount.Value));
                for (var i = 0; i < VisitorCount.Value; i++)
                {
                    _visitors.Add(new RuinVisiter(m_io, this, m_root));
                }
                _group = new Group(m_io, this, m_root);
            }
            private StringRecord _ruinId;
            private StringRecord _title;
            private StringRecord _desc;
            private IntRecord _image;
            private Position _position;
            private BankRecord _cash;
            private StringRecord _item;
            private StringRecord _looter;
            private IntRecord _aipriority;
            private IntRecord _visitorCount;
            private List<RuinVisiter> _visitors;
            private Group _group;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord RuinId { get { return _ruinId; } }
            public StringRecord Title { get { return _title; } }
            public StringRecord Desc { get { return _desc; } }
            public IntRecord Image { get { return _image; } }
            public Position Position { get { return _position; } }
            public BankRecord Cash { get { return _cash; } }
            public StringRecord Item { get { return _item; } }
            public StringRecord Looter { get { return _looter; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public IntRecord VisitorCount { get { return _visitorCount; } }
            public List<RuinVisiter> Visitors { get { return _visitors; } }
            public Group Group { get { return _group; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidScenVariables : KaitaiStruct
        {
            public static MidScenVariables FromFile(string fileName)
            {
                return new MidScenVariables(new KaitaiStream(fileName));
            }

            public MidScenVariables(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _variablesCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _variables = new List<SceneVariable>(SgRead.Capacity(VariablesCount.Value));
                for (var i = 0; i < VariablesCount.Value; i++)
                {
                    _variables.Add(new SceneVariable(m_io, this, m_root));
                }
            }
            private IntRecord _variablesCount;
            private List<SceneVariable> _variables;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord VariablesCount { get { return _variablesCount; } }
            public List<SceneVariable> Variables { get { return _variables; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidSiteMage : KaitaiStruct
        {
            public static MidSiteMage FromFile(string fileName)
            {
                return new MidSiteMage(new KaitaiStream(fileName));
            }

            public MidSiteMage(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _site = new Site(m_io, this, m_root);
                _spellCount = new IntRecord("QTY_SPELL", m_io, this, m_root);
                _spells = new List<StringRecord>(SgRead.Capacity(SpellCount.Value));
                for (var i = 0; i < SpellCount.Value; i++)
                {
                    _spells.Add(new StringRecord("SPELL_ID", m_io, this, m_root));
                }
                _visitorCount = new IntRecord("SnnnSInnnn", m_io, this, m_root);
                _visitors = new List<SiteVisitor>(SgRead.Capacity(VisitorCount.Value));
                for (var i = 0; i < VisitorCount.Value; i++)
                {
                    _visitors.Add(new SiteVisitor(m_io, this, m_root));
                }
            }
            private Site _site;
            private IntRecord _spellCount;
            private List<StringRecord> _spells;
            private IntRecord _visitorCount;
            private List<SiteVisitor> _visitors;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public Site Site { get { return _site; } }
            public IntRecord SpellCount { get { return _spellCount; } }
            public List<StringRecord> Spells { get { return _spells; } }
            public IntRecord VisitorCount { get { return _visitorCount; } }
            public List<SiteVisitor> Visitors { get { return _visitors; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidSiteMerchant : KaitaiStruct
        {
            public static MidSiteMerchant FromFile(string fileName)
            {
                return new MidSiteMerchant(new KaitaiStream(fileName));
            }

            public MidSiteMerchant(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _site = new Site(m_io, this, m_root);
                _buyArmor = new BoolRecord("BUY_ARMOR", m_io, this, m_root);
                _buyJewel = new BoolRecord("BUY_JEWEL", m_io, this, m_root);
                _buyWeapon = new BoolRecord("BUY_WEAPON", m_io, this, m_root);
                _buyBanner = new BoolRecord("BUY_BANNER", m_io, this, m_root);
                _buyPotion = new BoolRecord("BUY_POTION", m_io, this, m_root);
                _buyScroll = new BoolRecord("BUY_SCROLL", m_io, this, m_root);
                _buyWand = new BoolRecord("BUY_WAND", m_io, this, m_root);
                _buyValue = new BoolRecord("BUY_VALUE", m_io, this, m_root);
                _itemCount = new IntRecord("QTY_ITEM", m_io, this, m_root);
                _items = new List<MerchantItemEntry>(SgRead.Capacity(ItemCount.Value));
                for (var i = 0; i < ItemCount.Value; i++)
                {
                    _items.Add(new MerchantItemEntry(m_io, this, m_root));
                }
                _mission = new BoolRecord("MISSION", m_io, this, m_root);
                _visitorCount = new IntRecord("SnnnSInnnn", m_io, this, m_root);
                _visitors = new List<SiteVisitor>(SgRead.Capacity(VisitorCount.Value));
                for (var i = 0; i < VisitorCount.Value; i++)
                {
                    _visitors.Add(new SiteVisitor(m_io, this, m_root));
                }
            }
            private Site _site;
            private BoolRecord _buyArmor;
            private BoolRecord _buyJewel;
            private BoolRecord _buyWeapon;
            private BoolRecord _buyBanner;
            private BoolRecord _buyPotion;
            private BoolRecord _buyScroll;
            private BoolRecord _buyWand;
            private BoolRecord _buyValue;
            private IntRecord _itemCount;
            private List<MerchantItemEntry> _items;
            private BoolRecord _mission;
            private IntRecord _visitorCount;
            private List<SiteVisitor> _visitors;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public Site Site { get { return _site; } }
            public BoolRecord BuyArmor { get { return _buyArmor; } }
            public BoolRecord BuyJewel { get { return _buyJewel; } }
            public BoolRecord BuyWeapon { get { return _buyWeapon; } }
            public BoolRecord BuyBanner { get { return _buyBanner; } }
            public BoolRecord BuyPotion { get { return _buyPotion; } }
            public BoolRecord BuyScroll { get { return _buyScroll; } }
            public BoolRecord BuyWand { get { return _buyWand; } }
            public BoolRecord BuyValue { get { return _buyValue; } }
            public IntRecord ItemCount { get { return _itemCount; } }
            public List<MerchantItemEntry> Items { get { return _items; } }
            public BoolRecord Mission { get { return _mission; } }
            public IntRecord VisitorCount { get { return _visitorCount; } }
            public List<SiteVisitor> Visitors { get { return _visitors; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidSiteMercs : KaitaiStruct
        {
            public static MidSiteMercs FromFile(string fileName)
            {
                return new MidSiteMercs(new KaitaiStream(fileName));
            }

            public MidSiteMercs(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _site = new Site(m_io, this, m_root);
                _unitCount = new IntRecord("QTY_UNIT", m_io, this, m_root);
                _units = new List<MercsUnitEntry>(SgRead.Capacity(UnitCount.Value));
                for (var i = 0; i < UnitCount.Value; i++)
                {
                    _units.Add(new MercsUnitEntry(m_io, this, m_root));
                }
                _visitorCount = new IntRecord("SnnnSInnnn", m_io, this, m_root);
                _visitors = new List<SiteVisitor>(SgRead.Capacity(VisitorCount.Value));
                for (var i = 0; i < VisitorCount.Value; i++)
                {
                    _visitors.Add(new SiteVisitor(m_io, this, m_root));
                }
            }
            private Site _site;
            private IntRecord _unitCount;
            private List<MercsUnitEntry> _units;
            private IntRecord _visitorCount;
            private List<SiteVisitor> _visitors;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public Site Site { get { return _site; } }
            public IntRecord UnitCount { get { return _unitCount; } }
            public List<MercsUnitEntry> Units { get { return _units; } }
            public IntRecord VisitorCount { get { return _visitorCount; } }
            public List<SiteVisitor> Visitors { get { return _visitors; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidSiteTrainer : KaitaiStruct
        {
            public static MidSiteTrainer FromFile(string fileName)
            {
                return new MidSiteTrainer(new KaitaiStream(fileName));
            }

            public MidSiteTrainer(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _site = new Site(m_io, this, m_root);
                _visitorCount = new IntRecord("SnnnSInnnn", m_io, this, m_root);
                _visitors = new List<SiteVisitor>(SgRead.Capacity(VisitorCount.Value));
                for (var i = 0; i < VisitorCount.Value; i++)
                {
                    _visitors.Add(new SiteVisitor(m_io, this, m_root));
                }
            }
            private Site _site;
            private IntRecord _visitorCount;
            private List<SiteVisitor> _visitors;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public Site Site { get { return _site; } }
            public IntRecord VisitorCount { get { return _visitorCount; } }
            public List<SiteVisitor> Visitors { get { return _visitors; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidSpellCast : KaitaiStruct
        {
            public static MidSpellCast FromFile(string fileName)
            {
                return new MidSpellCast(new KaitaiStream(fileName));
            }

            public MidSpellCast(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _count = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _items = new List<MidSpellCastItem>(SgRead.Capacity(Count.Value));
                for (var i = 0; i < Count.Value; i++)
                {
                    _items.Add(new MidSpellCastItem(m_io, this, m_root));
                }
                _itemCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _itemCasts = new List<MidSpellCastItem>(SgRead.Capacity(ItemCount.Value));
                for (var i = 0; i < ItemCount.Value; i++)
                {
                    _itemCasts.Add(new MidSpellCastItem(m_io, this, m_root));
                }
            }
            private IntRecord _count;
            private List<MidSpellCastItem> _items;
            private IntRecord _itemCount;
            private List<MidSpellCastItem> _itemCasts;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord Count { get { return _count; } }
            public List<MidSpellCastItem> Items { get { return _items; } }
            public IntRecord ItemCount { get { return _itemCount; } }
            public List<MidSpellCastItem> ItemCasts { get { return _itemCasts; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidSpellCastItem : KaitaiStruct
        {
            public static MidSpellCastItem FromFile(string fileName)
            {
                return new MidSpellCastItem(new KaitaiStream(fileName));
            }

            public MidSpellCastItem(KaitaiStream p__io, Sg.MidSpellCast p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _playerId = new StringRecord("PLAYER_ID", m_io, this, m_root);
                _origin = new StringRecord("ORIGIN", m_io, this, m_root);
                _turnStop = new IntRecord("TURN_STOP", m_io, this, m_root);
            }
            private StringRecord _playerId;
            private StringRecord _origin;
            private IntRecord _turnStop;
            private Sg m_root;
            private Sg.MidSpellCast m_parent;
            public StringRecord PlayerId { get { return _playerId; } }
            public StringRecord Origin { get { return _origin; } }
            public IntRecord TurnStop { get { return _turnStop; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidSpellCast M_Parent { get { return m_parent; } }
        }
        public partial class MidSpellEffectItem : KaitaiStruct
        {
            public static MidSpellEffectItem FromFile(string fileName)
            {
                return new MidSpellEffectItem(new KaitaiStream(fileName));
            }

            public MidSpellEffectItem(KaitaiStream p__io, Sg.MidSpellEffects p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _unitId = new StringRecord("UNIT_ID", m_io, this, m_root);
                _originId = new StringRecord("ORIGIN_ID", m_io, this, m_root);
                _modifId = new StringRecord("MODIF_ID", m_io, this, m_root);
                _casterId = new StringRecord("CASTER_ID", m_io, this, m_root);
                _turnStop = new IntRecord("TURN_STOP", m_io, this, m_root);
            }
            private StringRecord _unitId;
            private StringRecord _originId;
            private StringRecord _modifId;
            private StringRecord _casterId;
            private IntRecord _turnStop;
            private Sg m_root;
            private Sg.MidSpellEffects m_parent;
            public StringRecord UnitId { get { return _unitId; } }
            public StringRecord OriginId { get { return _originId; } }
            public StringRecord ModifId { get { return _modifId; } }
            public StringRecord CasterId { get { return _casterId; } }
            public IntRecord TurnStop { get { return _turnStop; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidSpellEffects M_Parent { get { return m_parent; } }
        }
        public partial class MidSpellEffects : KaitaiStruct
        {
            public static MidSpellEffects FromFile(string fileName)
            {
                return new MidSpellEffects(new KaitaiStream(fileName));
            }

            public MidSpellEffects(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _count = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _items = new List<MidSpellEffectItem>(SgRead.Capacity(Count.Value));
                for (var i = 0; i < Count.Value; i++)
                {
                    _items.Add(new MidSpellEffectItem(m_io, this, m_root));
                }
            }
            private IntRecord _count;
            private List<MidSpellEffectItem> _items;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord Count { get { return _count; } }
            public List<MidSpellEffectItem> Items { get { return _items; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidStack : KaitaiStruct
        {
            public static MidStack FromFile(string fileName)
            {
                return new MidStack(new KaitaiStream(fileName));
            }

            public MidStack(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _group = new Group(m_io, this, m_root);
                _itemCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _items = new List<StringRecord>(SgRead.Capacity(ItemCount.Value));
                for (var i = 0; i < ItemCount.Value; i++)
                {
                    _items.Add(new StringRecord("ITEM_ID", m_io, this, m_root));
                }
                _stackId = new StringRecord("STACK_ID", m_io, this, m_root);
                _srctmplId = new StringRecord("SRCTMPL_ID", m_io, this, m_root);
                _leaderId = new StringRecord("LEADER_ID", m_io, this, m_root);
                _leaderAliv = new BoolRecord("LEADR_ALIV", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _morale = new IntRecord("MORALE", m_io, this, m_root);
                _move = new IntRecord("MOVE", m_io, this, m_root);
                _facing = new IntRecord("FACING", m_io, this, m_root);
                _banner = new StringRecord("BANNER", m_io, this, m_root);
                _tome = new StringRecord("TOME", m_io, this, m_root);
                _battle1 = new StringRecord("BATTLE1", m_io, this, m_root);
                _battle2 = new StringRecord("BATTLE2", m_io, this, m_root);
                _artifact1 = new StringRecord("ARTIFACT1", m_io, this, m_root);
                _artifact2 = new StringRecord("ARTIFACT2", m_io, this, m_root);
                _boots = new StringRecord("BOOTS", m_io, this, m_root);
                _owner = new StringRecord("OWNER", m_io, this, m_root);
                _inside = new StringRecord("INSIDE", m_io, this, m_root);
                _subrace = new StringRecord("SUBRACE", m_io, this, m_root);
                _invisible = new BoolRecord("INVISIBLE", m_io, this, m_root);
                _aiIgnore = new BoolRecord("AI_IGNORE", m_io, this, m_root);
                _upgcount = new IntRecord("UPGCOUNT", m_io, this, m_root);
                _order = new IntRecord("ORDER", m_io, this, m_root);
                _orderTarg = new StringRecord("ORDER_TARG", m_io, this, m_root);
                _aiorder = new IntRecord("AIORDER", m_io, this, m_root);
                _aiordertar = new StringRecord("AIORDERTAR", m_io, this, m_root);
                _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
                _creatLvl = new IntRecord("CREAT_LVL", m_io, this, m_root);
                _nbbattle = new IntRecord("NBBATTLE", m_io, this, m_root);
            }
            private Group _group;
            private IntRecord _itemCount;
            private List<StringRecord> _items;
            private StringRecord _stackId;
            private StringRecord _srctmplId;
            private StringRecord _leaderId;
            private BoolRecord _leaderAliv;
            private Position _position;
            private IntRecord _morale;
            private IntRecord _move;
            private IntRecord _facing;
            private StringRecord _banner;
            private StringRecord _tome;
            private StringRecord _battle1;
            private StringRecord _battle2;
            private StringRecord _artifact1;
            private StringRecord _artifact2;
            private StringRecord _boots;
            private StringRecord _owner;
            private StringRecord _inside;
            private StringRecord _subrace;
            private BoolRecord _invisible;
            private BoolRecord _aiIgnore;
            private IntRecord _upgcount;
            private IntRecord _order;
            private StringRecord _orderTarg;
            private IntRecord _aiorder;
            private StringRecord _aiordertar;
            private IntRecord _aipriority;
            private IntRecord _creatLvl;
            private IntRecord _nbbattle;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public Group Group { get { return _group; } }
            public IntRecord ItemCount { get { return _itemCount; } }
            public List<StringRecord> Items { get { return _items; } }
            public StringRecord StackId { get { return _stackId; } }
            public StringRecord SrctmplId { get { return _srctmplId; } }
            public StringRecord LeaderId { get { return _leaderId; } }
            public BoolRecord LeaderAliv { get { return _leaderAliv; } }
            public Position Position { get { return _position; } }
            public IntRecord Morale { get { return _morale; } }
            public IntRecord Move { get { return _move; } }
            public IntRecord Facing { get { return _facing; } }
            public StringRecord Banner { get { return _banner; } }
            public StringRecord Tome { get { return _tome; } }
            public StringRecord Battle1 { get { return _battle1; } }
            public StringRecord Battle2 { get { return _battle2; } }
            public StringRecord Artifact1 { get { return _artifact1; } }
            public StringRecord Artifact2 { get { return _artifact2; } }
            public StringRecord Boots { get { return _boots; } }
            public StringRecord Owner { get { return _owner; } }
            public StringRecord Inside { get { return _inside; } }
            public StringRecord Subrace { get { return _subrace; } }
            public BoolRecord Invisible { get { return _invisible; } }
            public BoolRecord AiIgnore { get { return _aiIgnore; } }
            public IntRecord Upgcount { get { return _upgcount; } }
            public IntRecord Order { get { return _order; } }
            public StringRecord OrderTarg { get { return _orderTarg; } }
            public IntRecord Aiorder { get { return _aiorder; } }
            public StringRecord Aiordertar { get { return _aiordertar; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public IntRecord CreatLvl { get { return _creatLvl; } }
            public IntRecord Nbbattle { get { return _nbbattle; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidStackDestroyed : KaitaiStruct
        {
            public static MidStackDestroyed FromFile(string fileName)
            {
                return new MidStackDestroyed(new KaitaiStream(fileName));
            }

            public MidStackDestroyed(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _count = new IntRecord("XxxxXXxxxx", m_io, this, m_root);
                _stacks = new List<MidStackDestroyedEntry>(SgRead.Capacity(Count.Value));
                for (var i = 0; i < Count.Value; i++)
                {
                    _stacks.Add(new MidStackDestroyedEntry(m_io, this, m_root));
                }
            }
            private IntRecord _count;
            private List<MidStackDestroyedEntry> _stacks;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord Count { get { return _count; } }
            public List<MidStackDestroyedEntry> Stacks { get { return _stacks; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidStackDestroyedEntry : KaitaiStruct
        {
            public static MidStackDestroyedEntry FromFile(string fileName)
            {
                return new MidStackDestroyedEntry(new KaitaiStream(fileName));
            }

            public MidStackDestroyedEntry(KaitaiStream p__io, Sg.MidStackDestroyed p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStack = new StringRecord("ID_STACK", m_io, this, m_root);
                _idKiller = new StringRecord("ID_KILLER", m_io, this, m_root);
                _srctmplId = new StringRecord("SRCTMPL_ID", m_io, this, m_root);
            }
            private StringRecord _idStack;
            private StringRecord _idKiller;
            private StringRecord _srctmplId;
            private Sg m_root;
            private Sg.MidStackDestroyed m_parent;
            public StringRecord IdStack { get { return _idStack; } }
            public StringRecord IdKiller { get { return _idKiller; } }
            public StringRecord SrctmplId { get { return _srctmplId; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidStackDestroyed M_Parent { get { return m_parent; } }
        }
        public partial class MidStackTemplate : KaitaiStruct
        {
            public static MidStackTemplate FromFile(string fileName)
            {
                return new MidStackTemplate(new KaitaiStream(fileName));
            }

            public MidStackTemplate(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _id = new StringRecord("ID", m_io, this, m_root);
                _owner = new StringRecord("OWNER", m_io, this, m_root);
                _leader = new StringRecord("LEADER", m_io, this, m_root);
                _leaderLvl = new IntRecord("LEADER_LVL", m_io, this, m_root);
                _nameTxt = new StringRecord("NAME_TXT", m_io, this, m_root);
                _orderTarg = new StringRecord("ORDER_TARG", m_io, this, m_root);
                _subrace = new StringRecord("SUBRACE", m_io, this, m_root);
                _order = new IntRecord("ORDER", m_io, this, m_root);
                _units = new List<StackTemplateUnitRecord>(SgRead.Capacity(6));
                for (var i = 0; i < 6; i++)
                {
                    _units.Add(new StackTemplateUnitRecord(m_io, this, m_root));
                }
                _positions = new List<IntRecord>(SgRead.Capacity(6));
                for (var i = 0; i < 6; i++)
                {
                    _positions.Add(new IntRecord("POS_N", m_io, this, m_root));
                }
                _useFacing = new BoolRecord("USE_FACING", m_io, this, m_root);
                _facing = new IntRecord("FACING", m_io, this, m_root);
                _modifierCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _modifiers = new List<UnitModifier>(SgRead.Capacity(ModifierCount.Value));
                for (var i = 0; i < ModifierCount.Value; i++)
                {
                    _modifiers.Add(new UnitModifier(m_io, this, m_root));
                }
                if (M_Root.NewBody) {
                    _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
                }
            }
            private StringRecord _id;
            private StringRecord _owner;
            private StringRecord _leader;
            private IntRecord _leaderLvl;
            private StringRecord _nameTxt;
            private StringRecord _orderTarg;
            private StringRecord _subrace;
            private IntRecord _order;
            private List<StackTemplateUnitRecord> _units;
            private List<IntRecord> _positions;
            private BoolRecord _useFacing;
            private IntRecord _facing;
            private IntRecord _modifierCount;
            private List<UnitModifier> _modifiers;
            private IntRecord _aipriority;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord Id { get { return _id; } }
            public StringRecord Owner { get { return _owner; } }
            public StringRecord Leader { get { return _leader; } }
            public IntRecord LeaderLvl { get { return _leaderLvl; } }
            public StringRecord NameTxt { get { return _nameTxt; } }
            public StringRecord OrderTarg { get { return _orderTarg; } }
            public StringRecord Subrace { get { return _subrace; } }
            public IntRecord Order { get { return _order; } }
            public List<StackTemplateUnitRecord> Units { get { return _units; } }
            public List<IntRecord> Positions { get { return _positions; } }
            public BoolRecord UseFacing { get { return _useFacing; } }
            public IntRecord Facing { get { return _facing; } }
            public IntRecord ModifierCount { get { return _modifierCount; } }
            public List<UnitModifier> Modifiers { get { return _modifiers; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidSubrace : KaitaiStruct
        {
            public static MidSubrace FromFile(string fileName)
            {
                return new MidSubrace(new KaitaiStream(fileName));
            }

            public MidSubrace(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _subraceId = new StringRecord("SUBRACE_ID", m_io, this, m_root);
                _subrace = new IntRecord("SUBRACE", m_io, this, m_root);
                _playerId = new StringRecord("PLAYER_ID", m_io, this, m_root);
                _number = new IntRecord("NUMBER", m_io, this, m_root);
                _nameTxt = new StringRecord("NAME_TXT", m_io, this, m_root);
                _banner = new IntRecord("BANNER", m_io, this, m_root);
            }
            private StringRecord _subraceId;
            private IntRecord _subrace;
            private StringRecord _playerId;
            private IntRecord _number;
            private StringRecord _nameTxt;
            private IntRecord _banner;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord SubraceId { get { return _subraceId; } }
            public IntRecord Subrace { get { return _subrace; } }
            public StringRecord PlayerId { get { return _playerId; } }
            public IntRecord Number { get { return _number; } }
            public StringRecord NameTxt { get { return _nameTxt; } }
            public IntRecord Banner { get { return _banner; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidTalismanCharges : KaitaiStruct
        {
            public static MidTalismanCharges FromFile(string fileName)
            {
                return new MidTalismanCharges(new KaitaiStream(fileName));
            }

            public MidTalismanCharges(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _chargesCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _charges = new List<TalismanCharge>(SgRead.Capacity(ChargesCount.Value));
                for (var i = 0; i < ChargesCount.Value; i++)
                {
                    _charges.Add(new TalismanCharge(m_io, this, m_root));
                }
            }
            private IntRecord _chargesCount;
            private List<TalismanCharge> _charges;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord ChargesCount { get { return _chargesCount; } }
            public List<TalismanCharge> Charges { get { return _charges; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidTomb : KaitaiStruct
        {
            public static MidTomb FromFile(string fileName)
            {
                return new MidTomb(new KaitaiStream(fileName));
            }

            public MidTomb(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _tombId = new StringRecord("TOMB_ID", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _entryCount = new IntRecord("QTY_EP", m_io, this, m_root);
                _entries = new List<MidTombEntry>(SgRead.Capacity(EntryCount.Value));
                for (var i = 0; i < EntryCount.Value; i++)
                {
                    _entries.Add(new MidTombEntry(m_io, this, m_root));
                }
            }
            private StringRecord _tombId;
            private Position _position;
            private IntRecord _entryCount;
            private List<MidTombEntry> _entries;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord TombId { get { return _tombId; } }
            public Position Position { get { return _position; } }
            public IntRecord EntryCount { get { return _entryCount; } }
            public List<MidTombEntry> Entries { get { return _entries; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidTombEntry : KaitaiStruct
        {
            public static MidTombEntry FromFile(string fileName)
            {
                return new MidTombEntry(new KaitaiStream(fileName));
            }

            public MidTombEntry(KaitaiStream p__io, Sg.MidTomb p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _stackOwner = new StringRecord("STACK_OWNR", m_io, this, m_root);
                _killer = new StringRecord("KILLER", m_io, this, m_root);
                _turn = new IntRecord("TURN", m_io, this, m_root);
                _stackName = new StringRecord("STACK_NAME", m_io, this, m_root);
            }
            private StringRecord _stackOwner;
            private StringRecord _killer;
            private IntRecord _turn;
            private StringRecord _stackName;
            private Sg m_root;
            private Sg.MidTomb m_parent;
            public StringRecord StackOwner { get { return _stackOwner; } }
            public StringRecord Killer { get { return _killer; } }
            public IntRecord Turn { get { return _turn; } }
            public StringRecord StackName { get { return _stackName; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidTomb M_Parent { get { return m_parent; } }
        }
        public partial class MidUnit : KaitaiStruct
        {
            public static MidUnit FromFile(string fileName)
            {
                return new MidUnit(new KaitaiStream(fileName));
            }

            public MidUnit(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _unitId = new StringRecord("UNIT_ID", m_io, this, m_root);
                _type = new StringRecord("TYPE", m_io, this, m_root);
                _level = new IntRecord("LEVEL", m_io, this, m_root);
                _modifierCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _modifiers = new List<StringRecord>(SgRead.Capacity(ModifierCount.Value));
                for (var i = 0; i < ModifierCount.Value; i++)
                {
                    _modifiers.Add(new StringRecord("MODIF_ID", m_io, this, m_root));
                }
                _creation = new IntRecord("CREATION", m_io, this, m_root);
                _nameTxt = new StringRecord("NAME_TXT", m_io, this, m_root);
                _transf = new BoolRecord("TRANSF", m_io, this, m_root);
                if (Transf.Value == 1) {
                    _origTypeId = new StringRecord("ORIGTYPEID", m_io, this, m_root);
                }
                if (Transf.Value == 1) {
                    _keepHp = new BoolRecord("KEEP_HP", m_io, this, m_root);
                }
                if (Transf.Value == 1) {
                    _origXp = new IntRecord("ORIG_XP", m_io, this, m_root);
                }
                if (Transf.Value == 1) {
                    _hpBefore = new IntRecord("HP_BEFORE", m_io, this, m_root);
                }
                if (Transf.Value == 1) {
                    _hpBeforeMax = new IntRecord("HP_BEF_MAX", m_io, this, m_root);
                }
                if ( ((M_Root.NewBody) || (((Sg.MidfileHeader) (M_Root.Header.Data)).Version == 35)) ) {
                    _dynlevel = new BoolRecord("DYNLEVEL", m_io, this, m_root);
                }
                _hp = new IntRecord("HP", m_io, this, m_root);
                _xp = new IntRecord("XP", m_io, this, m_root);
            }
            private StringRecord _unitId;
            private StringRecord _type;
            private IntRecord _level;
            private IntRecord _modifierCount;
            private List<StringRecord> _modifiers;
            private IntRecord _creation;
            private StringRecord _nameTxt;
            private BoolRecord _transf;
            private StringRecord _origTypeId;
            private BoolRecord _keepHp;
            private IntRecord _origXp;
            private IntRecord _hpBefore;
            private IntRecord _hpBeforeMax;
            private BoolRecord _dynlevel;
            private IntRecord _hp;
            private IntRecord _xp;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord UnitId { get { return _unitId; } }
            public StringRecord Type { get { return _type; } }
            public IntRecord Level { get { return _level; } }
            public IntRecord ModifierCount { get { return _modifierCount; } }
            public List<StringRecord> Modifiers { get { return _modifiers; } }
            public IntRecord Creation { get { return _creation; } }
            public StringRecord NameTxt { get { return _nameTxt; } }
            public BoolRecord Transf { get { return _transf; } }

            /// <summary>
            /// Id of original unit 'GnnnUUnnnn'
            /// </summary>
            public StringRecord OrigTypeId { get { return _origTypeId; } }
            public BoolRecord KeepHp { get { return _keepHp; } }
            public IntRecord OrigXp { get { return _origXp; } }
            public IntRecord HpBefore { get { return _hpBefore; } }
            public IntRecord HpBeforeMax { get { return _hpBeforeMax; } }
            public BoolRecord Dynlevel { get { return _dynlevel; } }
            public IntRecord Hp { get { return _hp; } }
            public IntRecord Xp { get { return _xp; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidVillage : KaitaiStruct
        {
            public static MidVillage FromFile(string fileName)
            {
                return new MidVillage(new KaitaiStream(fileName));
            }

            public MidVillage(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _city = new City(m_io, this, m_root);
                _itemCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _items = new List<StringRecord>(SgRead.Capacity(ItemCount.Value));
                for (var i = 0; i < ItemCount.Value; i++)
                {
                    _items.Add(new StringRecord("ITEM_ID", m_io, this, m_root));
                }
                _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
                _protectB = new StringRecord("PROTECT_B", m_io, this, m_root);
                _regenB = new IntRecord("REGEN_B", m_io, this, m_root);
                _morale = new IntRecord("MORALE", m_io, this, m_root);
                _growthT = new IntRecord("GROWTH_T", m_io, this, m_root);
                _size = new IntRecord("SIZE", m_io, this, m_root);
                _pOUn = new BoolRecord("P_O_UN", m_io, this, m_root);
                _pOHe = new BoolRecord("P_O_HE", m_io, this, m_root);
                _pOHu = new BoolRecord("P_O_HU", m_io, this, m_root);
                _pODw = new BoolRecord("P_O_DW", m_io, this, m_root);
                if (M_Root.Header.Signature == "D2EESFISIG") {
                    _pOEl = new BoolRecord("P_O_EL", m_io, this, m_root);
                }
                _riotT = new IntRecord("RIOT_T", m_io, this, m_root);
            }
            private City _city;
            private IntRecord _itemCount;
            private List<StringRecord> _items;
            private IntRecord _aipriority;
            private StringRecord _protectB;
            private IntRecord _regenB;
            private IntRecord _morale;
            private IntRecord _growthT;
            private IntRecord _size;
            private BoolRecord _pOUn;
            private BoolRecord _pOHe;
            private BoolRecord _pOHu;
            private BoolRecord _pODw;
            private BoolRecord _pOEl;
            private IntRecord _riotT;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public City City { get { return _city; } }
            public IntRecord ItemCount { get { return _itemCount; } }
            public List<StringRecord> Items { get { return _items; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public StringRecord ProtectB { get { return _protectB; } }
            public IntRecord RegenB { get { return _regenB; } }
            public IntRecord Morale { get { return _morale; } }
            public IntRecord GrowthT { get { return _growthT; } }
            public IntRecord Size { get { return _size; } }
            public BoolRecord POUn { get { return _pOUn; } }
            public BoolRecord POHe { get { return _pOHe; } }
            public BoolRecord POHu { get { return _pOHu; } }
            public BoolRecord PODw { get { return _pODw; } }
            public BoolRecord POEl { get { return _pOEl; } }
            public IntRecord RiotT { get { return _riotT; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidfileHeader : KaitaiStruct
        {
            public static MidfileHeader FromFile(string fileName)
            {
                return new MidfileHeader(new KaitaiStream(fileName));
            }

            public MidfileHeader(KaitaiStream p__io, Sg.SgHeader p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _zeroes = m_io.ReadBytes(6);
                _version = m_io.ReadUInt16();
                _unknown = m_io.ReadBytes((Version == 0 ? 2981 : 2725));
            }
            private byte[] _zeroes;
            private ushort _version;
            private byte[] _unknown;
            private Sg m_root;
            private Sg.SgHeader m_parent;
            public byte[] Zeroes { get { return _zeroes; } }
            public ushort Version { get { return _version; } }

            /// <summary>
            /// disciples-godot: у заголовка версии 0 (главы кампании «Эллион», game-dev\Campaign) — на 256 байт длиннее:
            /// счётчик объектов стоит на 2999, а не на 2743 (замер всех глав MidFile четырёх своих кампаний).
            /// </summary>
            public byte[] Unknown { get { return _unknown; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.SgHeader M_Parent { get { return m_parent; } }
        }
        public partial class MidgardMap : KaitaiStruct
        {
            public static MidgardMap FromFile(string fileName)
            {
                return new MidgardMap(new KaitaiStream(fileName));
            }

            public MidgardMap(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _unknown = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
            }
            private IntRecord _unknown;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord Unknown { get { return _unknown; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidgardMapBlock : KaitaiStruct
        {
            public static MidgardMapBlock FromFile(string fileName)
            {
                return new MidgardMapBlock(new KaitaiStream(fileName));
            }

            public MidgardMapBlock(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _blockid = new StringRecord("BLOCKID", m_io, this, m_root);
                _blockdata = new IntRecord("BLOCKDATA", m_io, this, m_root);
                _tiles = new List<Tile>(SgRead.Capacity(Blockdata.Value / 4));
                for (var i = 0; i < Blockdata.Value / 4; i++)
                {
                    _tiles.Add(new Tile(m_io, this, m_root));
                }
            }
            private StringRecord _blockid;
            private IntRecord _blockdata;
            private List<Tile> _tiles;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord Blockid { get { return _blockid; } }
            public IntRecord Blockdata { get { return _blockdata; } }
            public List<Tile> Tiles { get { return _tiles; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidgardMapFog : KaitaiStruct
        {
            public static MidgardMapFog FromFile(string fileName)
            {
                return new MidgardMapFog(new KaitaiStream(fileName));
            }

            public MidgardMapFog(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _count = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _entries = new List<FogEntry>(SgRead.Capacity(Count.Value));
                for (var i = 0; i < Count.Value; i++)
                {
                    _entries.Add(new FogEntry(m_io, this, m_root));
                }
            }
            private IntRecord _count;
            private List<FogEntry> _entries;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord Count { get { return _count; } }
            public List<FogEntry> Entries { get { return _entries; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MidgardPlan : KaitaiStruct
        {
            public static MidgardPlan FromFile(string fileName)
            {
                return new MidgardPlan(new KaitaiStream(fileName));
            }

            public MidgardPlan(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _unknown = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _count = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _entries = new List<PlanEntry>(SgRead.Capacity(Count.Value));
                for (var i = 0; i < Count.Value; i++)
                {
                    _entries.Add(new PlanEntry(m_io, this, m_root));
                }
            }
            private IntRecord _unknown;
            private IntRecord _count;
            private List<PlanEntry> _entries;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord Unknown { get { return _unknown; } }
            public IntRecord Count { get { return _count; } }
            public List<PlanEntry> Entries { get { return _entries; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class MountainsEntry : KaitaiStruct
        {
            public static MountainsEntry FromFile(string fileName)
            {
                return new MountainsEntry(new KaitaiStream(fileName));
            }

            public MountainsEntry(KaitaiStream p__io, Sg.MidMountains p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idMount = new IntRecord("ID_MOUNT", m_io, this, m_root);
                _size = new Size(m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _image = new IntRecord("IMAGE", m_io, this, m_root);
                _race = new IntRecord("RACE", m_io, this, m_root);
            }
            private IntRecord _idMount;
            private Size _size;
            private Position _position;
            private IntRecord _image;
            private IntRecord _race;
            private Sg m_root;
            private Sg.MidMountains m_parent;
            public IntRecord IdMount { get { return _idMount; } }
            public Size Size { get { return _size; } }
            public Position Position { get { return _position; } }
            public IntRecord Image { get { return _image; } }
            public IntRecord Race { get { return _race; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidMountains M_Parent { get { return m_parent; } }
        }
        public partial class PlanEntry : KaitaiStruct
        {
            public static PlanEntry FromFile(string fileName)
            {
                return new PlanEntry(new KaitaiStream(fileName));
            }

            public PlanEntry(KaitaiStream p__io, Sg.MidgardPlan p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _position = new Position(m_io, this, m_root);
                _element = new StringRecord("ELEMENT", m_io, this, m_root);
            }
            private Position _position;
            private StringRecord _element;
            private Sg m_root;
            private Sg.MidgardPlan m_parent;
            public Position Position { get { return _position; } }
            public StringRecord Element { get { return _element; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidgardPlan M_Parent { get { return m_parent; } }
        }
        public partial class PlayerBuildings : KaitaiStruct
        {
            public static PlayerBuildings FromFile(string fileName)
            {
                return new PlayerBuildings(new KaitaiStream(fileName));
            }

            public PlayerBuildings(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _buildingsCount = new IntRecord("XxxxXXxxxx", m_io, this, m_root);
                _buildings = new List<StringRecord>(SgRead.Capacity(BuildingsCount.Value));
                for (var i = 0; i < BuildingsCount.Value; i++)
                {
                    _buildings.Add(new StringRecord("BUILD_ID", m_io, this, m_root));
                }
            }
            private IntRecord _buildingsCount;
            private List<StringRecord> _buildings;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord BuildingsCount { get { return _buildingsCount; } }
            public List<StringRecord> Buildings { get { return _buildings; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class PlayerKnownSpells : KaitaiStruct
        {
            public static PlayerKnownSpells FromFile(string fileName)
            {
                return new PlayerKnownSpells(new KaitaiStream(fileName));
            }

            public PlayerKnownSpells(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _spellsCount = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
                _spells = new List<StringRecord>(SgRead.Capacity(SpellsCount.Value));
                for (var i = 0; i < SpellsCount.Value; i++)
                {
                    _spells.Add(new StringRecord("SPELL_ID", m_io, this, m_root));
                }
            }
            private IntRecord _spellsCount;
            private List<StringRecord> _spells;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord SpellsCount { get { return _spellsCount; } }
            public List<StringRecord> Spells { get { return _spells; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }

        /// <summary>
        /// Represents map coordinates
        /// </summary>
        public partial class Position : KaitaiStruct
        {
            public static Position FromFile(string fileName)
            {
                return new Position(new KaitaiStream(fileName));
            }

            public Position(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _x = new IntRecord("POS_X", m_io, this, m_root);
                _y = new IntRecord("POS_Y", m_io, this, m_root);
            }
            private IntRecord _x;
            private IntRecord _y;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public IntRecord X { get { return _x; } }
            public IntRecord Y { get { return _y; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class RaceInfo : KaitaiStruct
        {
            public static RaceInfo FromFile(string fileName)
            {
                return new RaceInfo(new KaitaiStream(fileName));
            }

            public RaceInfo(KaitaiStream p__io, Sg.D2eeHeader p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _raceId = m_io.ReadUInt32();
                _unknown = m_io.ReadBytes(36);
            }
            private uint _raceId;
            private byte[] _unknown;
            private Sg m_root;
            private Sg.D2eeHeader m_parent;

            /// <summary>
            /// Id encoded the same way as in 'scenario_info.player_' fields
            /// </summary>
            public uint RaceId { get { return _raceId; } }
            public byte[] Unknown { get { return _unknown; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.D2eeHeader M_Parent { get { return m_parent; } }
        }
        public partial class RuinVisiter : KaitaiStruct
        {
            public static RuinVisiter FromFile(string fileName)
            {
                return new RuinVisiter(new KaitaiStream(fileName));
            }

            public RuinVisiter(KaitaiStream p__io, Sg.MidRuin p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _ruinId = new StringRecord("RUIN_ID", m_io, this, m_root);
                _visiter = new StringRecord("VISITER", m_io, this, m_root);
            }
            private StringRecord _ruinId;
            private StringRecord _visiter;
            private Sg m_root;
            private Sg.MidRuin m_parent;
            public StringRecord RuinId { get { return _ruinId; } }
            public StringRecord Visiter { get { return _visiter; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidRuin M_Parent { get { return m_parent; } }
        }
        public partial class ScenarioInfo : KaitaiStruct
        {
            public static ScenarioInfo FromFile(string fileName)
            {
                return new ScenarioInfo(new KaitaiStream(fileName));
            }

            public ScenarioInfo(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _infoId = new StringRecord("INFO_ID", m_io, this, m_root);
                _campaign = new StringRecord("CAMPAIGN", m_io, this, m_root);
                _sourceM = new BoolRecord("SOURCE_M", m_io, this, m_root);
                _qtyCities = new IntRecord("QTY_CITIES", m_io, this, m_root);
                _name = new StringRecord("NAME", m_io, this, m_root);
                _desc = new StringRecord("DESC", m_io, this, m_root);
                _briefing = new StringRecord("BRIEFING", m_io, this, m_root);
                _debunkw = new StringRecord("DEBUNKW", m_io, this, m_root);
                if ( ((M_Root.NewBody) || (((Sg.MidfileHeader) (M_Root.Header.Data)).Version == 35)) ) {
                    _debunkw2 = new StringRecord("DEBUNKW2", m_io, this, m_root);
                }
                if ( ((M_Root.NewBody) || (((Sg.MidfileHeader) (M_Root.Header.Data)).Version == 35)) ) {
                    _debunkw3 = new StringRecord("DEBUNKW3", m_io, this, m_root);
                }
                if ( ((M_Root.NewBody) || (((Sg.MidfileHeader) (M_Root.Header.Data)).Version == 35)) ) {
                    _debunkw4 = new StringRecord("DEBUNKW4", m_io, this, m_root);
                }
                if ( ((M_Root.NewBody) || (((Sg.MidfileHeader) (M_Root.Header.Data)).Version == 35)) ) {
                    _debunkw5 = new StringRecord("DEBUNKW5", m_io, this, m_root);
                }
                _debunkl = new StringRecord("DEBUNKL", m_io, this, m_root);
                _brieflong1 = new StringRecord("BRIEFLONG1", m_io, this, m_root);
                _brieflong2 = new StringRecord("BRIEFLONG2", m_io, this, m_root);
                _brieflong3 = new StringRecord("BRIEFLONG3", m_io, this, m_root);
                _brieflong4 = new StringRecord("BRIEFLONG4", m_io, this, m_root);
                _brieflong5 = new StringRecord("BRIEFLONG5", m_io, this, m_root);
                _o = new BoolRecord("O", m_io, this, m_root);
                _curTurn = new IntRecord("CUR_TURN", m_io, this, m_root);
                _maxUnit = new IntRecord("MAX_UNIT", m_io, this, m_root);
                _maxSpell = new IntRecord("MAX_SPELL", m_io, this, m_root);
                _maxLeader = new IntRecord("MAX_LEADER", m_io, this, m_root);
                _maxCity = new IntRecord("MAX_CITY", m_io, this, m_root);
                _mapSize = new IntRecord("MAP_SIZE", m_io, this, m_root);
                _diffscen = new IntRecord("DIFFSCEN", m_io, this, m_root);
                _diffgame = new IntRecord("DIFFGAME", m_io, this, m_root);
                _creator = new StringRecord("CREATOR", m_io, this, m_root);
                _players = new List<IntRecord>(SgRead.Capacity(13));
                for (var i = 0; i < 13; i++)
                {
                    switch (i) {
                    case 10: {
                        _players.Add(new IntRecord("PLAYER_NN", m_io, this, m_root));
                        break;
                    }
                    case 11: {
                        _players.Add(new IntRecord("PLAYER_NN", m_io, this, m_root));
                        break;
                    }
                    case 12: {
                        _players.Add(new IntRecord("PLAYER_NN", m_io, this, m_root));
                        break;
                    }
                    case 9: {
                        _players.Add(new IntRecord("PLAYER_NN", m_io, this, m_root));
                        break;
                    }
                    default: {
                        _players.Add(new IntRecord("PLAYER_N", m_io, this, m_root));
                        break;
                    }
                    }
                }
                _suggLvl = new IntRecord("SUGG_LVL", m_io, this, m_root);
                _mapSeed = new IntRecord("MAP_SEED", m_io, this, m_root);
            }
            private StringRecord _infoId;
            private StringRecord _campaign;
            private BoolRecord _sourceM;
            private IntRecord _qtyCities;
            private StringRecord _name;
            private StringRecord _desc;
            private StringRecord _briefing;
            private StringRecord _debunkw;
            private StringRecord _debunkw2;
            private StringRecord _debunkw3;
            private StringRecord _debunkw4;
            private StringRecord _debunkw5;
            private StringRecord _debunkl;
            private StringRecord _brieflong1;
            private StringRecord _brieflong2;
            private StringRecord _brieflong3;
            private StringRecord _brieflong4;
            private StringRecord _brieflong5;
            private BoolRecord _o;
            private IntRecord _curTurn;
            private IntRecord _maxUnit;
            private IntRecord _maxSpell;
            private IntRecord _maxLeader;
            private IntRecord _maxCity;
            private IntRecord _mapSize;
            private IntRecord _diffscen;
            private IntRecord _diffgame;
            private StringRecord _creator;
            private List<IntRecord> _players;
            private IntRecord _suggLvl;
            private IntRecord _mapSeed;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public StringRecord InfoId { get { return _infoId; } }
            public StringRecord Campaign { get { return _campaign; } }
            public BoolRecord SourceM { get { return _sourceM; } }
            public IntRecord QtyCities { get { return _qtyCities; } }
            public StringRecord Name { get { return _name; } }
            public StringRecord Desc { get { return _desc; } }
            public StringRecord Briefing { get { return _briefing; } }
            public StringRecord Debunkw { get { return _debunkw; } }
            public StringRecord Debunkw2 { get { return _debunkw2; } }
            public StringRecord Debunkw3 { get { return _debunkw3; } }
            public StringRecord Debunkw4 { get { return _debunkw4; } }
            public StringRecord Debunkw5 { get { return _debunkw5; } }
            public StringRecord Debunkl { get { return _debunkl; } }
            public StringRecord Brieflong1 { get { return _brieflong1; } }
            public StringRecord Brieflong2 { get { return _brieflong2; } }
            public StringRecord Brieflong3 { get { return _brieflong3; } }
            public StringRecord Brieflong4 { get { return _brieflong4; } }
            public StringRecord Brieflong5 { get { return _brieflong5; } }
            public BoolRecord O { get { return _o; } }
            public IntRecord CurTurn { get { return _curTurn; } }
            public IntRecord MaxUnit { get { return _maxUnit; } }
            public IntRecord MaxSpell { get { return _maxSpell; } }
            public IntRecord MaxLeader { get { return _maxLeader; } }
            public IntRecord MaxCity { get { return _maxCity; } }
            public IntRecord MapSize { get { return _mapSize; } }
            public IntRecord Diffscen { get { return _diffscen; } }
            public IntRecord Diffgame { get { return _diffgame; } }
            public StringRecord Creator { get { return _creator; } }
            public List<IntRecord> Players { get { return _players; } }
            public IntRecord SuggLvl { get { return _suggLvl; } }
            public IntRecord MapSeed { get { return _mapSeed; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class ScenarioObject : KaitaiStruct
        {
            public static ScenarioObject FromFile(string fileName)
            {
                return new ScenarioObject(new KaitaiStream(fileName));
            }

            public ScenarioObject(KaitaiStream p__io, Sg p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _what = new StringRecord("WHAT", m_io, this, m_root);
                _objId = new StringRecord("OBJ_ID", m_io, this, m_root);
                _begobject = new Begobject(m_io, this, m_root);
                switch (What.String) {
                case ".?AVCCapital@@": {
                    _data = new Capital(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidBag@@": {
                    _data = new MidBag(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidCrystal@@": {
                    _data = new MidCrystal(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidDiplomacy@@": {
                    _data = new MidDiplomacy(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidEvent@@": {
                    _data = new MidEvent(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidItem@@": {
                    _data = new MidItem(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidLandmark@@": {
                    _data = new MidLandmark(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidLocation@@": {
                    _data = new MidLocation(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidMountains@@": {
                    _data = new MidMountains(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidPlayer@@": {
                    _data = new MidPlayer(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidQuestLog@@": {
                    _data = new MidQuestLog(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidRoad@@": {
                    _data = new MidRoad(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidRod@@": {
                    _data = new MidRod(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidRuin@@": {
                    _data = new MidRuin(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidScenVariables@@": {
                    _data = new MidScenVariables(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidSiteMage@@": {
                    _data = new MidSiteMage(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidSiteMerchant@@": {
                    _data = new MidSiteMerchant(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidSiteMercs@@": {
                    _data = new MidSiteMercs(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidSiteTrainer@@": {
                    _data = new MidSiteTrainer(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidSpellCast@@": {
                    _data = new MidSpellCast(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidSpellEffects@@": {
                    _data = new MidSpellEffects(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidStack@@": {
                    _data = new MidStack(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidStackDestroyed@@": {
                    _data = new MidStackDestroyed(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidStackTemplate@@": {
                    _data = new MidStackTemplate(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidSubRace@@": {
                    _data = new MidSubrace(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidTalismanCharges@@": {
                    _data = new MidTalismanCharges(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidTomb@@": {
                    _data = new MidTomb(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidUnit@@": {
                    _data = new MidUnit(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidVillage@@": {
                    _data = new MidVillage(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidgardMap@@": {
                    _data = new MidgardMap(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidgardMapBlock@@": {
                    _data = new MidgardMapBlock(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidgardMapFog@@": {
                    _data = new MidgardMapFog(m_io, this, m_root);
                    break;
                }
                case ".?AVCMidgardPlan@@": {
                    _data = new MidgardPlan(m_io, this, m_root);
                    break;
                }
                case ".?AVCPlayerBuildings@@": {
                    _data = new PlayerBuildings(m_io, this, m_root);
                    break;
                }
                case ".?AVCPlayerKnownSpells@@": {
                    _data = new PlayerKnownSpells(m_io, this, m_root);
                    break;
                }
                case ".?AVCScenarioInfo@@": {
                    _data = new ScenarioInfo(m_io, this, m_root);
                    break;
                }
                case ".?AVCTurnSummary@@": {
                    _data = new TurnSummary(m_io, this, m_root);
                    break;
                }
                }
                _endobject = new Endobject(m_io, this, m_root);
            }
            private StringRecord _what;
            private StringRecord _objId;
            private Begobject _begobject;
            private KaitaiStruct _data;
            private Endobject _endobject;
            private Sg m_root;
            private Sg m_parent;
            public StringRecord What { get { return _what; } }
            public StringRecord ObjId { get { return _objId; } }
            public Begobject Begobject { get { return _begobject; } }
            public KaitaiStruct Data { get { return _data; } }
            public Endobject Endobject { get { return _endobject; } }
            public Sg M_Root { get { return m_root; } }
            public Sg M_Parent { get { return m_parent; } }
        }
        public partial class SceneVariable : KaitaiStruct
        {
            public static SceneVariable FromFile(string fileName)
            {
                return new SceneVariable(new KaitaiStream(fileName));
            }

            public SceneVariable(KaitaiStream p__io, Sg.MidScenVariables p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _id = new IntRecord("ID", m_io, this, m_root);
                _name = new StringRecord("NAME", m_io, this, m_root);
                _value = new IntRecord("VALUE", m_io, this, m_root);
            }
            private IntRecord _id;
            private StringRecord _name;
            private IntRecord _value;
            private Sg m_root;
            private Sg.MidScenVariables m_parent;
            public IntRecord Id { get { return _id; } }
            public StringRecord Name { get { return _name; } }
            public IntRecord Value { get { return _value; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidScenVariables M_Parent { get { return m_parent; } }
        }
        public partial class SgHeader : KaitaiStruct
        {
            public static SgHeader FromFile(string fileName)
            {
                return new SgHeader(new KaitaiStream(fileName));
            }

            public SgHeader(KaitaiStream p__io, Sg p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _signature = Cp1251.Encoding.GetString(KaitaiStream.BytesTerminate(m_io.ReadBytes(10), 0, false));
                switch (Signature) {
                case "D2EESFISIG": {
                    _data = new D2eeHeader(m_io, this, m_root);
                    break;
                }
                case "MidFile": {
                    _data = new MidfileHeader(m_io, this, m_root);
                    break;
                }
                }
            }
            private string _signature;
            private KaitaiStruct _data;
            private Sg m_root;
            private Sg m_parent;
            public string Signature { get { return _signature; } }
            public KaitaiStruct Data { get { return _data; } }
            public Sg M_Root { get { return m_root; } }
            public Sg M_Parent { get { return m_parent; } }
        }
        public partial class SingleId : KaitaiStruct
        {
            public static SingleId FromFile(string fileName)
            {
                return new SingleId(new KaitaiStream(fileName));
            }

            public SingleId(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _id = new IntRecord("XXXxxxXXXx", m_io, this, m_root);
            }
            private IntRecord _id;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public IntRecord Id { get { return _id; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class Site : KaitaiStruct
        {
            public static Site FromFile(string fileName)
            {
                return new Site(new KaitaiStream(fileName));
            }

            public Site(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _siteId = new StringRecord("SITE_ID", m_io, this, m_root);
                _imgIso = new IntRecord("IMG_ISO", m_io, this, m_root);
                _imgIntf = new StringRecord("IMG_INTF", m_io, this, m_root);
                _txtTitle = new StringRecord("TXT_TITLE", m_io, this, m_root);
                _txtDesc = new StringRecord("TXT_DESC", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                _visiter = new StringRecord("VISITER", m_io, this, m_root);
                _aipriority = new IntRecord("AIPRIORITY", m_io, this, m_root);
            }
            private StringRecord _siteId;
            private IntRecord _imgIso;
            private StringRecord _imgIntf;
            private StringRecord _txtTitle;
            private StringRecord _txtDesc;
            private Position _position;
            private StringRecord _visiter;
            private IntRecord _aipriority;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public StringRecord SiteId { get { return _siteId; } }
            public IntRecord ImgIso { get { return _imgIso; } }
            public StringRecord ImgIntf { get { return _imgIntf; } }
            public StringRecord TxtTitle { get { return _txtTitle; } }
            public StringRecord TxtDesc { get { return _txtDesc; } }
            public Position Position { get { return _position; } }
            public StringRecord Visiter { get { return _visiter; } }
            public IntRecord Aipriority { get { return _aipriority; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class SiteVisitor : KaitaiStruct
        {
            public static SiteVisitor FromFile(string fileName)
            {
                return new SiteVisitor(new KaitaiStream(fileName));
            }

            public SiteVisitor(KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _siteId = new StringRecord("SITE_ID", m_io, this, m_root);
                _visitor = new StringRecord("VISITER", m_io, this, m_root);
            }
            private StringRecord _siteId;
            private StringRecord _visitor;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public StringRecord SiteId { get { return _siteId; } }
            public StringRecord Visitor { get { return _visitor; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class Size : KaitaiStruct
        {
            public static Size FromFile(string fileName)
            {
                return new Size(new KaitaiStream(fileName));
            }

            public Size(KaitaiStream p__io, Sg.MountainsEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _x = new IntRecord("SIZE_X", m_io, this, m_root);
                _y = new IntRecord("SIZE_Y", m_io, this, m_root);
            }
            private IntRecord _x;
            private IntRecord _y;
            private Sg m_root;
            private Sg.MountainsEntry m_parent;
            public IntRecord X { get { return _x; } }
            public IntRecord Y { get { return _y; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MountainsEntry M_Parent { get { return m_parent; } }
        }
        public partial class StackTemplateUnitRecord : KaitaiStruct
        {
            public static StackTemplateUnitRecord FromFile(string fileName)
            {
                return new StackTemplateUnitRecord(new KaitaiStream(fileName));
            }

            public StackTemplateUnitRecord(KaitaiStream p__io, Sg.MidStackTemplate p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _unit = new StringRecord("UNIT_N", m_io, this, m_root);
                _level = new IntRecord("UNIT_N_LVL", m_io, this, m_root);
            }
            private StringRecord _unit;
            private IntRecord _level;
            private Sg m_root;
            private Sg.MidStackTemplate m_parent;
            public StringRecord Unit { get { return _unit; } }
            public IntRecord Level { get { return _level; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidStackTemplate M_Parent { get { return m_parent; } }
        }
        public partial class StringRecord : KaitaiStruct
        {
            public StringRecord(string p_nameValue, KaitaiStream p__io, KaitaiStruct p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _nameValue = p_nameValue;
                _read();
            }
            private void _read()
            {
                _name = SgRead.Name(m_io, NameValue);
                _length = m_io.ReadUInt32();
                _string = Cp1251.Encoding.GetString(KaitaiStream.BytesTerminate(m_io.ReadBytes(Length), 0, false));
            }
            private string _name;
            private uint _length;
            private string _string;
            private string _nameValue;
            private Sg m_root;
            private KaitaiStruct m_parent;
            public string Name { get { return _name; } }

            /// <summary>
            /// Length of string data, including trailing zero
            /// </summary>
            public uint Length { get { return _length; } }
            public string String { get { return _string; } }
            public string NameValue { get { return _nameValue; } }
            public Sg M_Root { get { return m_root; } }
            public KaitaiStruct M_Parent { get { return m_parent; } }
        }
        public partial class TalismanCharge : KaitaiStruct
        {
            public static TalismanCharge FromFile(string fileName)
            {
                return new TalismanCharge(new KaitaiStream(fileName));
            }

            public TalismanCharge(KaitaiStream p__io, Sg.MidTalismanCharges p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idTalis = new StringRecord("ID_TALIS", m_io, this, m_root);
                _charges = new IntRecord("CHARGES", m_io, this, m_root);
            }
            private StringRecord _idTalis;
            private IntRecord _charges;
            private Sg m_root;
            private Sg.MidTalismanCharges m_parent;
            public StringRecord IdTalis { get { return _idTalis; } }
            public IntRecord Charges { get { return _charges; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidTalismanCharges M_Parent { get { return m_parent; } }
        }
        public partial class Tile : KaitaiStruct
        {
            public static Tile FromFile(string fileName)
            {
                return new Tile(new KaitaiStream(fileName));
            }

            public Tile(KaitaiStream p__io, Sg.MidgardMapBlock p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _type = m_io.ReadU1();
                _unknown = m_io.ReadUInt16();
                _imageId = m_io.ReadU1();
            }
            private byte _type;
            private ushort _unknown;
            private byte _imageId;
            private Sg m_root;
            private Sg.MidgardMapBlock m_parent;
            public byte Type { get { return _type; } }
            public ushort Unknown { get { return _unknown; } }
            public byte ImageId { get { return _imageId; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidgardMapBlock M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummary : KaitaiStruct
        {
            public static TurnSummary FromFile(string fileName)
            {
                return new TurnSummary(new KaitaiStream(fileName));
            }

            public TurnSummary(KaitaiStream p__io, Sg.ScenarioObject p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _summaryCount = new IntRecord("SxxxTS0000", m_io, this, m_root);
                _summary = new List<TurnSummaryEntry>(SgRead.Capacity(SummaryCount.Value));
                for (var i = 0; i < SummaryCount.Value; i++)
                {
                    _summary.Add(new TurnSummaryEntry(m_io, this, m_root));
                }
            }
            private IntRecord _summaryCount;
            private List<TurnSummaryEntry> _summary;
            private Sg m_root;
            private Sg.ScenarioObject m_parent;
            public IntRecord SummaryCount { get { return _summaryCount; } }
            public List<TurnSummaryEntry> Summary { get { return _summary; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.ScenarioObject M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntry : KaitaiStruct
        {
            public static TurnSummaryEntry FromFile(string fileName)
            {
                return new TurnSummaryEntry(new KaitaiStream(fileName));
            }

            public TurnSummaryEntry(KaitaiStream p__io, Sg.TurnSummary p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idPlayer = new StringRecord("ID_PLAYER", m_io, this, m_root);
                _type = new IntRecord("TYPE", m_io, this, m_root);
                _position = new Position(m_io, this, m_root);
                switch (Type.Value) {
                case 0: {
                    _data = new TurnSummaryEntryType0(m_io, this, m_root);
                    break;
                }
                case 1: {
                    _data = new TurnSummaryEntryType1(m_io, this, m_root);
                    break;
                }
                case 2: {
                    _data = new TurnSummaryEntryType2(m_io, this, m_root);
                    break;
                }
                case 3: {
                    _data = new TurnSummaryEntryType3(m_io, this, m_root);
                    break;
                }
                case 4: {
                    _data = new TurnSummaryEntryType4(m_io, this, m_root);
                    break;
                }
                case 5: {
                    _data = new TurnSummaryEntryType5(m_io, this, m_root);
                    break;
                }
                case 6: {
                    _data = new TurnSummaryEntryType6(m_io, this, m_root);
                    break;
                }
                }
            }
            private StringRecord _idPlayer;
            private IntRecord _type;
            private Position _position;
            private KaitaiStruct _data;
            private Sg m_root;
            private Sg.TurnSummary m_parent;
            public StringRecord IdPlayer { get { return _idPlayer; } }
            public IntRecord Type { get { return _type; } }
            public Position Position { get { return _position; } }
            public KaitaiStruct Data { get { return _data; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummary M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntryType0 : KaitaiStruct
        {
            public static TurnSummaryEntryType0 FromFile(string fileName)
            {
                return new TurnSummaryEntryType0(new KaitaiStream(fileName));
            }

            public TurnSummaryEntryType0(KaitaiStream p__io, Sg.TurnSummaryEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idPlayer2 = new StringRecord("ID_PLAYER2", m_io, this, m_root);
                _idSpell = new StringRecord("ID_SPELL", m_io, this, m_root);
                _idStkD = new StringRecord("ID_STK_D", m_io, this, m_root);
                _strStkD = new StringRecord("STR_STK_D", m_io, this, m_root);
            }
            private StringRecord _idPlayer2;
            private StringRecord _idSpell;
            private StringRecord _idStkD;
            private StringRecord _strStkD;
            private Sg m_root;
            private Sg.TurnSummaryEntry m_parent;
            public StringRecord IdPlayer2 { get { return _idPlayer2; } }
            public StringRecord IdSpell { get { return _idSpell; } }
            public StringRecord IdStkD { get { return _idStkD; } }
            public StringRecord StrStkD { get { return _strStkD; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummaryEntry M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntryType1 : KaitaiStruct
        {
            public static TurnSummaryEntryType1 FromFile(string fileName)
            {
                return new TurnSummaryEntryType1(new KaitaiStream(fileName));
            }

            public TurnSummaryEntryType1(KaitaiStream p__io, Sg.TurnSummaryEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idPlayer2 = new StringRecord("ID_PLAYER2", m_io, this, m_root);
                _idStkA = new StringRecord("ID_STK_A", m_io, this, m_root);
                _strStkA = new StringRecord("STR_STK_A", m_io, this, m_root);
                _idStkD = new StringRecord("ID_STK_D", m_io, this, m_root);
                _strStkD = new StringRecord("STR_STK_D", m_io, this, m_root);
            }
            private StringRecord _idPlayer2;
            private StringRecord _idStkA;
            private StringRecord _strStkA;
            private StringRecord _idStkD;
            private StringRecord _strStkD;
            private Sg m_root;
            private Sg.TurnSummaryEntry m_parent;
            public StringRecord IdPlayer2 { get { return _idPlayer2; } }
            public StringRecord IdStkA { get { return _idStkA; } }
            public StringRecord StrStkA { get { return _strStkA; } }
            public StringRecord IdStkD { get { return _idStkD; } }
            public StringRecord StrStkD { get { return _strStkD; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummaryEntry M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntryType2 : KaitaiStruct
        {
            public static TurnSummaryEntryType2 FromFile(string fileName)
            {
                return new TurnSummaryEntryType2(new KaitaiStream(fileName));
            }

            public TurnSummaryEntryType2(KaitaiStream p__io, Sg.TurnSummaryEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStkD = new StringRecord("ID_STK_D", m_io, this, m_root);
                _strStkD = new StringRecord("STR_STK_D", m_io, this, m_root);
            }
            private StringRecord _idStkD;
            private StringRecord _strStkD;
            private Sg m_root;
            private Sg.TurnSummaryEntry m_parent;
            public StringRecord IdStkD { get { return _idStkD; } }
            public StringRecord StrStkD { get { return _strStkD; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummaryEntry M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntryType3 : KaitaiStruct
        {
            public static TurnSummaryEntryType3 FromFile(string fileName)
            {
                return new TurnSummaryEntryType3(new KaitaiStream(fileName));
            }

            public TurnSummaryEntryType3(KaitaiStream p__io, Sg.TurnSummaryEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idCity = new StringRecord("ID_CITY", m_io, this, m_root);
            }
            private StringRecord _idCity;
            private Sg m_root;
            private Sg.TurnSummaryEntry m_parent;
            public StringRecord IdCity { get { return _idCity; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummaryEntry M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntryType4 : KaitaiStruct
        {
            public static TurnSummaryEntryType4 FromFile(string fileName)
            {
                return new TurnSummaryEntryType4(new KaitaiStream(fileName));
            }

            public TurnSummaryEntryType4(KaitaiStream p__io, Sg.TurnSummaryEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
            }
            private Sg m_root;
            private Sg.TurnSummaryEntry m_parent;
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummaryEntry M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntryType5 : KaitaiStruct
        {
            public static TurnSummaryEntryType5 FromFile(string fileName)
            {
                return new TurnSummaryEntryType5(new KaitaiStream(fileName));
            }

            public TurnSummaryEntryType5(KaitaiStream p__io, Sg.TurnSummaryEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStkD = new StringRecord("ID_STK_D", m_io, this, m_root);
                _strStkD = new StringRecord("STR_STK_D", m_io, this, m_root);
            }
            private StringRecord _idStkD;
            private StringRecord _strStkD;
            private Sg m_root;
            private Sg.TurnSummaryEntry m_parent;
            public StringRecord IdStkD { get { return _idStkD; } }
            public StringRecord StrStkD { get { return _strStkD; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummaryEntry M_Parent { get { return m_parent; } }
        }
        public partial class TurnSummaryEntryType6 : KaitaiStruct
        {
            public static TurnSummaryEntryType6 FromFile(string fileName)
            {
                return new TurnSummaryEntryType6(new KaitaiStream(fileName));
            }

            public TurnSummaryEntryType6(KaitaiStream p__io, Sg.TurnSummaryEntry p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _idStkD = new StringRecord("ID_STK_D", m_io, this, m_root);
                _strStkD = new StringRecord("STR_STK_D", m_io, this, m_root);
                _acquire = new BoolRecord("ACQUIRE", m_io, this, m_root);
            }
            private StringRecord _idStkD;
            private StringRecord _strStkD;
            private BoolRecord _acquire;
            private Sg m_root;
            private Sg.TurnSummaryEntry m_parent;
            public StringRecord IdStkD { get { return _idStkD; } }
            public StringRecord StrStkD { get { return _strStkD; } }
            public BoolRecord Acquire { get { return _acquire; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.TurnSummaryEntry M_Parent { get { return m_parent; } }
        }
        public partial class UnitModifier : KaitaiStruct
        {
            public static UnitModifier FromFile(string fileName)
            {
                return new UnitModifier(new KaitaiStream(fileName));
            }

            public UnitModifier(KaitaiStream p__io, Sg.MidStackTemplate p__parent = null, Sg p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _unitPos = new IntRecord("UNIT_POS", m_io, this, m_root);
                _modifId = new StringRecord("MODIF_ID", m_io, this, m_root);
            }
            private IntRecord _unitPos;
            private StringRecord _modifId;
            private Sg m_root;
            private Sg.MidStackTemplate m_parent;
            public IntRecord UnitPos { get { return _unitPos; } }
            public StringRecord ModifId { get { return _modifId; } }
            public Sg M_Root { get { return m_root; } }
            public Sg.MidStackTemplate M_Parent { get { return m_parent; } }
        }
        private bool f_newBody;
        private bool _newBody;

        /// <summary>
        /// disciples-godot: поля, которых нет в теле старых файлов MidFile (версии 34 и 35), есть в D2EESFISIG и в MidFile версии 0
        /// (главы кампании «Эллион», game-dev\Campaign: у шаблонов отрядов AIPRIORITY, у ориентиров DESC_TXT — как в новом формате).
        /// </summary>
        public bool NewBody
        {
            get
            {
                if (f_newBody)
                    return _newBody;
                f_newBody = true;
                _newBody = (bool) ( ((Header.Signature == "D2EESFISIG") || (((Sg.MidfileHeader) (Header.Data)).Version == 0)) );
                return _newBody;
            }
        }
        private SgHeader _header;
        private IntRecord _total;
        private List<ScenarioObject> _objects;
        private Sg m_root;
        private KaitaiStruct m_parent;
        public SgHeader Header { get { return _header; } }
        public IntRecord Total { get { return _total; } }
        public List<ScenarioObject> Objects { get { return _objects; } }
        public Sg M_Root { get { return m_root; } }
        public KaitaiStruct M_Parent { get { return m_parent; } }
    }
}
