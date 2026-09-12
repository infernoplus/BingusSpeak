using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

#nullable enable

namespace BingusSpeak
{
    public class CellWorker
    {
        private static Cell? MakeCell (JsonNode node, ESM esm)
        {
            bool is_interior = node["data"]!["flags"]!.GetValue<string>().ToLower().Contains("is_interior");
            int x = int.Parse(node["data"]!["grid"]![0]!.ToString());
            int y = int.Parse(node["data"]!["grid"]![1]!.ToString());

            Cell cell = new(esm, node);

            // If the cell is basically empty, we just go ahead and discard it.
            if (cell.contents.Count() <= 0) { return null; }

            return cell;
        }

        public static (List<Cell>, List<Cell>) Go(ESM esm, List<JsonNode> cellRecords)
        {
            var cells = cellRecords.AsParallel()
                .WithDegreeOfParallelism(Const.THREAD_COUNT)
                .Select(node => {
                        Cell cell = MakeCell(node, esm)!;
                        return cell;
                    })
                .Where(cell => cell != null);

            /* Grab all parsed cells from threads and put em in lists */
            List<Cell> interior = new();
            List<Cell> exterior = new();
            foreach (var cell in cells)
            {
                if (Math.Abs(cell!.coordinate.x) > Const.CELL_EXTERIOR_BOUNDS || Math.Abs(cell.coordinate.y) > Const.CELL_EXTERIOR_BOUNDS || cell.HasFlag(Cell.Flag.IsInterior))
                {
                    interior.Add(cell);
                }
                else
                {
                    exterior.Add(cell);
                }
            }

            /* Sort arrays since they were loaded in threads the order is effectively random */
            /* Sorting is not strictly required but it causes randomization in the process which we want to avoid */
            interior = interior.OrderBy(c => c.name).ToList();
            exterior = exterior.OrderByDescending(c => c.coordinate.x).ThenByDescending(c => c.coordinate.y).ToList();

            /* return */
            return (exterior, interior);
        }
    }
}
