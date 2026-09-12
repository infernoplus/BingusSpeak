using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using static BingusSpeak.Types;

namespace BingusSpeak
{
    [DebuggerDisplay("{name} :: [{coordinate.x},{coordinate.y}]")]
    public class Cell
    {
        public enum Flag { IsInterior, HasWater, RestingIsIllegal, BehavesLikeExterior, Unk40 }

        public readonly string name;
        public readonly string region;
        public readonly Int2 coordinate;  // Position on the cell grid
        public readonly Vector3 center;
        public readonly Vector3 boundsMin;
        public readonly Vector3 boundsMax;

        public readonly List<Flag> flags;

        public readonly List<Content> contents;            // All of this
        public readonly List<CreatureContent> creatures;
        public readonly List<NpcContent> npcs;

        public Cell(ESM esm, JsonNode json)
        {
            /* Cell Data */
            name = json["name"]?.ToString();
            region = json["region"]?.ToString();

            flags = new();
            string[] fs = json["data"]["flags"].GetValue<string>().ToLower().Split("|");
            foreach(string f in fs)
            {
                string trim = f.Trim().ToLower().Replace("_", "");
                if(trim == "0x40") { trim = "unk40"; }
                Flag flag = Enum.Parse<Flag>(trim, true);
                flags.Add(flag);
            }

            int x = int.Parse(json["data"]["grid"][0].ToString());
            int y = int.Parse(json["data"]["grid"][1].ToString());
            coordinate = new Int2(x, y);

            float half = Const.CELL_SIZE / 2f;
            center = new Vector3(coordinate.x, 0.0f, coordinate.y) * Const.CELL_SIZE + new Vector3(half, 0f, half);

            /* Cell Content Data */
            contents = new();
            creatures = new();
            npcs = new();

            foreach (JsonNode reference in json["references"].AsArray())
            {
                string id = reference["id"].ToString();
                Record record = esm.FindRecordById(id);

                if(record == null) { continue; }

                switch (record.type)
                {
                    case ESM.Type.Npc:
                        npcs.Add(new NpcContent(esm, this, reference, record));
                        break;
                    case ESM.Type.Creature:
                        creatures.Add(new CreatureContent(esm, this, reference, record));
                        break;
                    case ESM.Type.LeveledCreature:
                        //Record resolvedRecord = esm.ResolveLeveledCreature(id, Override.GetDifficultyScalar(this));
                        //if (resolvedRecord.type == ESM.Type.Creature) { creatures.Add(new CreatureContent(esm, this, reference, resolvedRecord)); }
                        //else if (resolvedRecord.type == ESM.Type.Npc) { npcs.Add(new NpcContent(esm, this, reference, resolvedRecord)); }
                        //else { throw new Exception("Invalid leveled list result record type"); } // if this ever happens todd howard owes me a blood sacrifice
                        break;
                    default: break; // skip everything not needed for dialog stuff
                }
            }

            contents.AddRange(creatures);
            contents.AddRange(npcs);


            /* Calculate bounding box */
            float x1 = float.MaxValue, y1 = float.MaxValue, z1 = float.MaxValue, x2 = float.MinValue, y2 = float.MinValue, z2 = float.MinValue;
            foreach (Content content in contents)
            {
                x1 = Math.Min(x1, content.position.X);
                y1 = Math.Min(y1, content.position.Y);
                z1 = Math.Min(z1, content.position.Z);
                x2 = Math.Max(x2, content.position.X);
                y2 = Math.Max(y2, content.position.Y);
                z2 = Math.Max(z2, content.position.Z);
            }
            const float PAD = 10f; // originally was multiplying but that resulted in the box being moved when all 4 points existed in the same quadrant (XY). padding is easier and safe
            boundsMin = new Vector3(x1, y1, z1) - new Vector3(PAD); // this is calc'd before we load models so we can't get a perfectly accurate bounding box. so we just pad it a bit and call it a day
            boundsMax = new Vector3(x2, y2, z2) + new Vector3(PAD);
        }

        public bool HasFlag(Flag flag)
        {
            return flags.Contains(flag);
        }

        public bool IsPointInside(Vector3 point)
        {
            float startX = center.X - Const.CELL_SIZE;
            float endX = center.X;
            float startY = center.Z - Const.CELL_SIZE;
            float endY = center.Z;

            Vector3 min = new(startX, 0f, startY);
            Vector3 max = new(endX, 0f, endY);
            if (point.X < min.X || point.X > max.X) return false;
            if(point.Z < min.Z || point.Z > max.Z) return false;

            return true;
        }

        public bool IsPointInside(List<Vector3> points)
        {
            foreach(Vector3 point in points)
            {
                if (IsPointInside(point))
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsExterior()
        {
            return !flags.Contains(Flag.IsInterior);
        }
    }
}
