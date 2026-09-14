using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static BingusSpeak.Dialog;

namespace BingusSpeak
{
    public class ESM
    {
        /* Types of records in the ESM */
        public enum Type
        {
            Header, GameSetting, GlobalVariable, Class, Faction, Race, Sound, Skill, MagicEffect, Script, Region, Birthsign, LandscapeTexture, Spell, Static, Door,
            MiscItem, Weapon, Container, Creature, Bodypart, Light, Enchanting, Npc, Armor, Clothing, RepairItem, Activator, Apparatus, Lockpick, Probe, Ingredient,
            Book, Alchemy, LeveledItem, LeveledCreature, Cell, Landscape, PathGrid, SoundGen, Dialogue, DialogueInfo
        }

        private readonly Dictionary<Type, Dictionary<string, JsonNode>> records;
        public List<Cell> exterior, interior;
        public List<RaceInfo> races;
        public List<JobInfo> jobs;  // classes, but we cant really use that word so 'job'
        public List<FactionInfo> factions;
        public List<DialogRecord> dialog;

        public ESM(string jsonPath)
        {
            string tempRawJson = File.ReadAllText(jsonPath);
            JsonArray json = JsonNode.Parse(tempRawJson).AsArray();

            records = new Dictionary<Type, Dictionary<string, JsonNode>>();
            var enumNames = Enum.GetNames(typeof(Type)).ToHashSet();

            foreach (string name in Enum.GetNames(typeof(Type)))
            {
                Enum.TryParse(name, out Type type);
                if (type == Type.Dialogue || type == Type.DialogueInfo) { continue; } // special records, need to be handled specially
                records.Add(type, new Dictionary<string, JsonNode>());
            }

            List<JsonNode> cellRecords = new();

            foreach (var record in json)
            {
                if (record?["type"] == null)
                {
                    continue;
                }

                var rawRecordType = record["type"].ToString();
                if (!enumNames.Contains(rawRecordType))
                {
                    continue;
                }
                if (!Enum.TryParse(rawRecordType, out Type type))
                {
                    continue;
                }

                // special records, need to be handled specially
                if (type is Type.Dialogue or Type.DialogueInfo)
                {
                    continue;
                }

                if (record["id"] != null)
                {
                    records[type].Add(record["id"].GetValue<string>().ToLower(), record);
                }
                else if(type == Type.Cell) { cellRecords.Add(record); }
            }

            /* Load raceinfo and jobinfo */
            races = new();
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Race))
            {
                races.Add(new(jsonNode));
            }

            jobs = new();
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Class))
            {
                jobs.Add(new(jsonNode));
            }

            /* Load faction info from esm */
            factions = new();
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Faction))
            {
                FactionInfo faction = new(jsonNode);
                factions.Add(faction);
            }

            /* Multi threading to speed this up... */
            (exterior, interior) = CellWorker.Go(this, cellRecords);

            /* Handle dialog stuff now */
            dialog = new();
            DialogRecord current = null;
            for (int i = 0; i < json.Count; i++)
            {
                JsonNode record = json[i];
                Enum.TryParse(record["type"].ToString(), out Type type);

                if (type == Type.Dialogue)
                {
                    string idstr = record["id"].ToString().Trim();
                    string typestr = idstr.Replace(" ", "");
                    string diatype = record["dialogue_type"].ToString();
                    typestr = new String(typestr.Where(c => c != '-' && (c < '0' || c > '9')).ToArray());
                    if (!Enum.TryParse(typestr, out DialogRecord.Type dtype)) { dtype = DialogRecord.Type.Topic; }
                    if (diatype.ToLower() == "journal") { dtype = DialogRecord.Type.Journal; }

                    if (current != null && current.type == DialogRecord.Type.Greeting && dtype == DialogRecord.Type.Greeting) { continue; } // skip so we can merge all 9 greeting levels into a single thingy

                    current = new(dtype, idstr);
                    dialog.Add(current);
                }
                else if (type == Type.DialogueInfo)
                {
                    // check for a "choice" filter and mark this as a Choice type dialoginforecord if that's the case
                    // choice type dialoginfo are only accessed through a choice papyrus call and have to be handled differently than other dialoginfos
                    bool isChoice = false;
                    foreach (JsonNode filterNode in record["filters"].AsArray())
                    {
                        if (filterNode["filter_type"].ToString() == "Function" && filterNode["function"].ToString() == "Choice") { isChoice = true; break; }
                    }

                    DialogInfoRecord dialogInfoRecord = new(isChoice ? DialogRecord.Type.Choice : current.type, record);
                    current.infos.Add(dialogInfoRecord);
                }
            }
        }

        /* Has to be seperate from the constructor as after we construct the ESM we inject some replacement text and additional dialog lines from a json file */
        public void PostProcessDialogStuff()
        {
            /* Iterate through all characters in the game, find all their dialog, then mark every line they use so we have a mapping of all dialogs used by all chars */
            foreach (Cell cell in exterior.Concat(interior))
            {
                foreach (Content content in cell.contents)
                {
                    if (content is not CharacterContent cc) { continue; }
                    if (cc.dead) { continue; } // skip dead npcs for various reasons

                    var dialog = GetDialog(cc);
                    foreach (var (topic, infos) in dialog)
                    {
                        foreach (DialogInfoRecord info in infos)
                        {
                            info.used.Add(cc);

                            // Also real quick check if any of their dialog lines are unique to them via the speaker_id field. Mark as important if they are
                            if (!cc.important && info.speaker != null && info.speaker.ToLower() == cc.id.ToLower()) { cc.important = true; }
                        }
                    }
                }
            }
        }

        /* Checks if a creature has any dialog associated to it and returns true/false. */
        /* This is an expensive and commonly used check so we cache the result for reuse */
        private Dictionary<string, bool> hasDialogCache = new();
        public bool HasDialog(CreatureContent content)
        {
            if (hasDialogCache.ContainsKey(content.id)) { return hasDialogCache[content.id]; }

            foreach (DialogRecord record in dialog)
            {
                foreach (DialogInfoRecord info in record.infos)
                {
                    if (info.speaker == content.id) { hasDialogCache.Add(content.id, true); return true; }
                }
            }

            hasDialogCache.Add(content.id, false);
            return false;
        }

        /* Get dialog and character data for building esd */
        public List<Tuple<DialogRecord, List<DialogInfoRecord>>> GetDialog(CharacterContent npc)
        {
            if(npc is CreatureContent creature && !HasDialog(creature)) { return new(); } // performance hack

            List<Tuple<DialogRecord, List<DialogInfoRecord>>> ds = new();  // i am really sorry about this type
            foreach (DialogRecord dialogRecord in dialog)
            {
                if (dialogRecord.type == DialogRecord.Type.Journal) { continue; } // obviously skip these lmao

                // Check if the npc meets requirements for any lines in this topic
                List<DialogInfoRecord> infos = new();
                foreach (DialogInfoRecord info in dialogRecord.infos)
                {
                    if (info.type == DialogRecord.Type.Flee) { continue; } // discarding this for now
                    if (info.type == DialogRecord.Type.Intruder) { continue; } // discarding this for now

                    if (npc.race == CharacterContent.Race.Creature && info.speaker != npc.id) { continue; } // creatures only have lines with the speaker condition set for them explicitly

                    // Check if the npc meets all static requirements for this dialog line. this includes resolving some filter to see if they can ever pass
                    if (info.IsUnreachableFor(npc)) { continue; }

                    infos.Add(info);

                    // If this line has no filters it means that anything below it is unreachable, so we just break in that case
                    if (info.filters.Count() <= 0 && info.playerFaction == null && info.playerRank <= 0 && info.disposition <= 0) { break; }
                }

                if (infos.Count() > 0) { ds.Add(new(dialogRecord, infos)); } // discard if no valid lines
            }

            return ds;
        }

        /* Returns a specific dialoginfo by it's true id */
        public DialogInfoRecord GetDialogInfo(Int128 id)
        {
            foreach (DialogRecord record in dialog)
            {
                foreach (DialogInfoRecord info in record.infos)
                {
                    if (info.trueId == id) { return info; }
                }
            }
            return null;
        }

        /* Return a DialogRecord by it's topic id */
        public DialogRecord GetTopic(string id)
        {
            foreach (DialogRecord record in dialog)
            {
                if (record.id.ToLower() == id.ToLower()) { return record; }
            }
            return null;
        }

        /* List of types that we should search for references */
        public readonly Type[] VALID_CONTENT_TYPES = {
            Type.Static, Type.Container, Type.Light, Type.Sound, Type.Skill, Type.Region, Type.Door, Type.MiscItem, Type.Weapon,  Type.Creature, Type.Bodypart, Type.Npc,
            Type.Armor, Type.Clothing, Type.RepairItem, Type.Activator, Type.Apparatus, Type.Lockpick, Type.Probe, Type.Ingredient, Type.Book, Type.Alchemy, Type.LeveledItem,
            Type.LeveledCreature, Type.PathGrid, Type.SoundGen
        };

        /* References don't contain any explicit 'type' data so... we just gotta go find it lol */
        public Record FindRecordById(string id)
        {
            foreach (var type in VALID_CONTENT_TYPES)
            {
                var recordsById = records[type];
                if (recordsById.TryGetValue(id.ToLower(), out var value))
                {
                    return new Record(type, value);
                }
            }
            return null; // Not found!
        }

        public IEnumerable<JsonNode> GetAllRecordsByType(Type type)
        {
            return records[type].Values;
        }

        public JobInfo GetJob(string id) => jobs.FirstOrDefault(job => job.id == id.ToLower());

        public RaceInfo GetRace(string id) => races.FirstOrDefault(race => race.id == id.ToLower());

        public FactionInfo GetFaction(string id) => factions.FirstOrDefault(faction => faction.id == id?.ToLower());
    }

    public class RaceInfo
    {
        public readonly string id, name, description;
        public readonly Dictionary<CharacterContent.Stats.Attribute, Dictionary<CharacterContent.Sex, int>> attributes;
        public readonly Dictionary<CharacterContent.Stats.Skill, int> skills;

        public RaceInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            name = json["name"].GetValue<string>();
            description = json["description"].GetValue<string>();

            attributes = new();
            skills = new();

            foreach (CharacterContent.Stats.Attribute attribute in Enum.GetValues(typeof(CharacterContent.Stats.Attribute)))
            {
                Dictionary<CharacterContent.Sex, int> values = new();

                JsonArray jary = json["data"][attribute.ToString().ToLower()].AsArray();
                values.Add(CharacterContent.Sex.Male, jary[0].GetValue<int>());
                values.Add(CharacterContent.Sex.Female, jary[1].GetValue<int>());

                attributes.Add(attribute, values);

            }

            for (int i = 0; i <= 6; i++)  // 7 is the number of skills a race can have as thier 'bonus' skills. hardcoded to esm. indexed as skill_0 to skill_6
            {
                string s = json["data"]["skill_bonuses"][$"skill_{i}"].GetValue<string>();
                if (s.ToLower() == "none") { continue; }
                CharacterContent.Stats.Skill skill = (CharacterContent.Stats.Skill)System.Enum.Parse(typeof(CharacterContent.Stats.Skill), s);
                int value = json["data"]["skill_bonuses"][$"bonus_{i}"].GetValue<int>();
                skills.Add(skill, value);
            }
        }

        public int GetAttribute(CharacterContent.Sex sex, CharacterContent.Stats.Attribute attribute) { return attributes[attribute][sex]; }
        public int GetSkill(CharacterContent.Stats.Skill skill) { if (skills.ContainsKey(skill)) { return skills[skill]; } else { return 0; } }
    }

    public class FactionInfo
    {
        public readonly string id, name;
        public readonly List<Rank> ranks;
        private readonly List<(string id, int value)> reactions;

        public FactionInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            name = json["name"].GetValue<string>();

            ranks = new();
            JsonArray rankNames = json["rank_names"].AsArray();
            JsonArray rankRequirements = json["data"]["requirements"].AsArray();
            for (int i = 0; i < rankNames.Count(); i++)
            {
                string rankName = rankNames[i].GetValue<string>();
                JsonNode rankRequiremnt = rankRequirements[i];
                int reputation = rankRequiremnt["reputation"].GetValue<int>();
                Rank rank = new(rankName, i + 1, reputation);
                ranks.Add(rank);
            }

            reactions = new();
            JsonArray reacts = json["reactions"].AsArray();
            for (int i = 0; i < reacts.Count(); i++)
            {
                JsonNode entry = reacts[i];
                reactions.Add((entry["faction"].GetValue<string>().ToLower(), entry["reaction"].GetValue<int>()));
            }
        }

        public string GetRankName(int rank) { if (rank < 0) { return "nobody"; } if (rank >= ranks.Count()) { return "member"; } return ranks[rank].name; }
        public int GetMaxRank()
        {
            int max = 0;
            foreach (Rank rank in ranks) { if (max < rank.level) { max = rank.level; } }
            return max;
        }

        public List<(string id, int value)> GetHighReactions()
        {
            // Copy and sort array then return.
            List<(string id, int reaction)> highs = new();
            highs.AddRange(reactions);
            highs.Sort((x, y) => y.reaction.CompareTo(x.reaction));
            return highs;
        }

        public List<(string id, int value)> GetLowReactions()
        {
            // Copy and sort array then return.
            List<(string id, int reaction)> lows = new();
            lows.AddRange(reactions);
            lows.Sort((x, y) => x.reaction.CompareTo(y.reaction));
            return lows;
        }

        public bool HasReactions()
        {
            return reactions.Count > 0;
        }

        public class Rank
        {
            public readonly string name;
            public readonly int level, reputation; // required reputation to reach this rank
            public Rank(string name, int level, int reputation)
            {
                this.name = name;
                this.level = level;
                this.reputation = reputation;
            }
        }
    }

    public class JobInfo
    {
        public enum Specialization
        {
            Combat, Stealth, Magic
        }

        public readonly string id, name, description;
        private readonly Specialization specialization;
        private readonly List<CharacterContent.Stats.Attribute> attributes;
        private readonly List<CharacterContent.Stats.Skill> major, minor;
        private readonly List<CharacterContent.Service> services;

        public JobInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            name = json["name"].GetValue<string>();
            description = json["description"].GetValue<string>();
            specialization = Enum.Parse<Specialization>(json["data"]["specialization"].GetValue<string>());

            attributes = new();
            major = new();
            minor = new();
            services = new();

            attributes.Add(Enum.Parse<CharacterContent.Stats.Attribute>(json["data"]["attribute1"].GetValue<string>()));
            attributes.Add(Enum.Parse<CharacterContent.Stats.Attribute>(json["data"]["attribute2"].GetValue<string>()));
            for (int i = 1; i <= 5; i++)
            {
                major.Add(Enum.Parse<CharacterContent.Stats.Skill>(json["data"][$"major{i}"].GetValue<string>()));
                minor.Add(Enum.Parse<CharacterContent.Stats.Skill>(json["data"][$"minor{i}"].GetValue<string>()));
            }
        }

        public bool HasAttribute(CharacterContent.Stats.Attribute attribute) { return attributes.Contains(attribute); }
        public bool HasMajor(CharacterContent.Stats.Skill skill) { return major.Contains(skill); }
        public bool HasMinor(CharacterContent.Stats.Skill skill) { return minor.Contains(skill); }
        public bool HasSpecialization(CharacterContent.Stats.Skill skill)
        {
            switch (skill)
            {
                case CharacterContent.Stats.Skill.Armorer:
                case CharacterContent.Stats.Skill.Athletics:
                case CharacterContent.Stats.Skill.Axe:
                case CharacterContent.Stats.Skill.Block:
                case CharacterContent.Stats.Skill.BluntWeapon:
                case CharacterContent.Stats.Skill.HeavyArmor:
                case CharacterContent.Stats.Skill.LongBlade:
                case CharacterContent.Stats.Skill.MediumArmor:
                case CharacterContent.Stats.Skill.Spear:
                    return specialization == Specialization.Combat;
                case CharacterContent.Stats.Skill.Acrobatics:
                case CharacterContent.Stats.Skill.HandToHand:
                case CharacterContent.Stats.Skill.LightArmor:
                case CharacterContent.Stats.Skill.Marksman:
                case CharacterContent.Stats.Skill.Mercantile:
                case CharacterContent.Stats.Skill.Security:
                case CharacterContent.Stats.Skill.ShortBlade:
                case CharacterContent.Stats.Skill.Sneak:
                case CharacterContent.Stats.Skill.Speechcraft:
                    return specialization == Specialization.Stealth;
                case CharacterContent.Stats.Skill.Alchemy:
                case CharacterContent.Stats.Skill.Alteration:
                case CharacterContent.Stats.Skill.Conjuration:
                case CharacterContent.Stats.Skill.Destruction:
                case CharacterContent.Stats.Skill.Enchant:
                case CharacterContent.Stats.Skill.Illusion:
                case CharacterContent.Stats.Skill.Mysticism:
                case CharacterContent.Stats.Skill.Restoration:
                case CharacterContent.Stats.Skill.Unarmored:
                    return specialization == Specialization.Magic;
                default:
                    throw new Exception("What the fuck");
            }
        }
    }

    public record Record(ESM.Type type, JsonNode json);
}
