using System;
using System.Collections.Generic;
using System.Linq;

namespace EvaluationMeasures.Measures
{
    public class BionicNDCG
    {
        private Dictionary<string, List<string>> testData;

        private int userCount;

        public static double getNDCG(IList<string> recommendations, IList<string> sequence, int n)
        {
            ISet<string> set = new HashSet<string>(sequence);
            List<ISet<string>> order = getItemsOrder(sequence);
            int itemsNumber = Math.Min(n, recommendations.Count);//If number of items in ranking list is smaller than n, we compute NDCG@Min(n, groundTruthSize)

            List<string> currentRecommendations = recommendations.ToList().GetRange(0, itemsNumber);
            List<int> ranks = getRankingList(order, currentRecommendations, set);//Ranks are not binary (Rank is a number of occurences in output list)
            IEnumerable<int> perfectOrder = new List<int>(ranks);

            perfectOrder = perfectOrder.OrderByDescending(i => i);

            double IDCG = getDCG(perfectOrder.ToList());

            double DCG = getDCG(ranks);

            return IDCG == 0.0 ? 0 : DCG/IDCG;
        }


        private static double getDCG(List<int> ranks)
        {
            double score = ranks[0];
            for (int i = 1; i < ranks.Count; i++)
            {
                score += ranks[i] / Math.Log(i + 1, 2);
            }

            return score;
        }

        private static List<int> getRankingList(List<ISet<string>> order, IEnumerable<string> items, ISet<string> testItems)
        {
            var ranks = new List<int>();

            foreach (var id in items)
            {
                if (!testItems.Contains(id))
                {
                    ranks.Add(0);
                    continue;
                }

                for (int i = 0; i < order.Count; i++)
                {
                    if (order[i].Contains(id))
                    {
                        ranks.Add(i + 1);
                    }
                }
            }

            return ranks;
        }

        private static List<ISet<string>> getItemsOrder(IEnumerable<string> sequence)
            //We sort it by popularity of items in case if items appear more than one time
        {
            var order = new List<ISet<string>>();
            var itemsMap = new Dictionary<string, int>();
            foreach (var id in sequence)//Count how many times same id's appear in sequence 
            {
                if (!itemsMap.ContainsKey(id))
                {
                    itemsMap.Add(id, 0);
                }

                itemsMap[id]++;
            }

            int max = int.MinValue;
            int min = int.MaxValue;

            foreach (var keyValue in itemsMap)
            {
                if (keyValue.Value > max)
                {
                    max = keyValue.Value;
                }

                if (keyValue.Value < min)
                {
                    min = keyValue.Value;
                }
            }

            for (var i = min; i <= max; i++)
            {
                var set = new HashSet<string>();

                foreach (var keyValue in itemsMap)
                {
                    if (keyValue.Value == i)
                    {
                        set.Add(keyValue.Key);
                    }
                }

                if (set.Count > 0)
                {
                    order.Add(set);
                }
            }

            return order;
        }
    }
}
