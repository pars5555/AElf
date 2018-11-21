﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.Kernel.Types;
using Akka.Util.Internal;
using NLog;
using Org.BouncyCastle.Security;
using SharpRepository.Repository.Caching.Hash;
using AElf.Kernel;
using AElf.Common;

namespace AElf.Execution.Scheduling
{
    /// <summary>
    /// The grouper can be used in both producing subgroup and splitting the job in batch
    /// </summary>
    public class Grouper : IGrouper
    {
        private IResourceUsageDetectionService _resourceUsageDetectionService;
        private ILogger _logger;

        public Grouper(IResourceUsageDetectionService resourceUsageDetectionService, ILogger logger = null)
        {
            _resourceUsageDetectionService = resourceUsageDetectionService;
            _logger = logger;
        }

        //TODO: for testnet we only have a single chain, thus grouper only take care of txList in one chain (hence Process has chainId as parameter)
        public async Task<Tuple<List<List<Transaction>>, Dictionary<Transaction, Exception>>> ProcessNaive(Hash chainId, List<Transaction> transactions)
        {
            var txResourceHandle = new Dictionary<Transaction, string>();
            var failedTxs = new Dictionary<Transaction, Exception>();
            if (transactions.Count == 0)
            {
                return new Tuple<List<List<Transaction>>, Dictionary<Transaction, Exception>>(new List<List<Transaction>>(), failedTxs);
            }

            Dictionary<string, UnionFindNode> resourceUnionSet = new Dictionary<string, UnionFindNode>();

	        //set up the union find set as the representation of graph and the connected components will be the resulting groups
            foreach (var tx in transactions)
            {
                UnionFindNode first = null;
                List<string> resources;
                try
                {
                    resources = (await _resourceUsageDetectionService.GetResources(chainId, tx)).ToList();
                }
                catch (Exception e)
                {
                    failedTxs.Add(tx, e);
                    continue;
                }
                
                //_logger.Debug(string.Format("tx {0} have resource [{1}]", tx.From, string.Join(" ||| ", resources)));
                foreach (var resource in resources)
                {
                    if (!resourceUnionSet.TryGetValue(resource, out var node))
                    {
                        node = new UnionFindNode();
                        resourceUnionSet.Add(resource, node);
                    }
                    if (first == null)
                    {
                        first = node;
                        txResourceHandle.Add(tx, resource);
                    }
                    else
                    {
                        node.Union(first);
                    }
                }
            }

            Dictionary<int, List<Transaction>> grouped = new Dictionary<int, List<Transaction>>();
            List<List<Transaction>> result = new List<List<Transaction>>();

            foreach (var tx in transactions)
            {
                if (txResourceHandle.TryGetValue(tx, out var firstResource))
                {
                    int nodeId = resourceUnionSet[firstResource].Find().NodeId;
                    if (!grouped.TryGetValue(nodeId, out var group))
                    {
                        group = new List<Transaction>();
                        grouped.Add(nodeId, group);
                    }
                    group.Add(tx);
                }
                else
                {
                    if (!failedTxs.ContainsKey(tx))
                    {
                        //each "resource-free" transaction have its own group
                        result.Add(new List<Transaction>(){tx});
                    }
                }
            }
            result.AddRange(grouped.Values);

            _logger?.Info(string.Format(
                "Grouper on chainId \"{0}\" group [{1}] transactions into [{2}] groups with sizes [{3}], There are also {4} transactions failed retriving resource", chainId.DumpHex(),
                transactions.Count, result.Count, string.Join(", ", result.Select(a=>a.Count)), failedTxs.Count));
            
            return new Tuple<List<List<Transaction>>, Dictionary<Transaction, Exception>>(result, failedTxs);
        }

        public async Task<Tuple<List<List<Transaction>>, Dictionary<Transaction, Exception>>> ProcessWithCoreCount(GroupStrategy strategy, int totalCores, Hash chainId, List<Transaction> transactions)
        {
            if (strategy == GroupStrategy.NaiveGroup)
            {
                return await ProcessNaive(chainId, transactions);
            }
            else
            {
                if (transactions.Count == 0)
                {
                    return new Tuple<List<List<Transaction>>, Dictionary<Transaction, Exception>>(new List<List<Transaction>>(), new Dictionary<Transaction, Exception>());
                }

                if (totalCores <= 0)
                {
                    throw new InvalidParameterException("Total core count " + totalCores + " is invalid");
                }
                
                var groupResults = await ProcessNaive(chainId, transactions);

                List<List<Transaction>> mergedGroups;

                if (strategy == GroupStrategy.Limited_MaxAddMins)
                {
                    mergedGroups = ProcessWithCoreCount_MaxAddMins(totalCores, groupResults.Item1);
                }
                else if(strategy == GroupStrategy.Limited_MinsAddUp)
                {
                    mergedGroups = ProcessWithCoreCount_MinsAddUp(totalCores, groupResults.Item1);
                }
                else
                {
                    mergedGroups = groupResults.Item1;
                    _logger?.Error("Grouper: unsupported strategy: " + strategy);
                }
                
                _logger?.Info(string.Format(
                    "Grouper on chainId [{0}] merge {1} groups into {2} groups with sizes [{3}]", chainId,
                    groupResults.Item1.Count, mergedGroups.Count, string.Join(", ", mergedGroups.Select(a=>a.Count))));


                return new Tuple<List<List<Transaction>>, Dictionary<Transaction, Exception>>(mergedGroups, groupResults.Item2);
            }
        }

        /// <summary>
        /// Rebalancing the groups, by add the two group with mininum groups size repeatly 
        /// </summary>
        /// <param name="totalCores"></param>
        /// <param name="unmergedGroups"></param>
        /// <returns></returns>
        public List<List<Transaction>> ProcessWithCoreCount_MinsAddUp(int totalCores, List<List<Transaction>> unmergedGroups)
        {
            if (unmergedGroups.Count <= 1 || unmergedGroups.Count <= totalCores)
            {
                return unmergedGroups;
            }
            else
            {
                
                var sortedList = unmergedGroups.OrderByDescending( a=> a.Count).ToList();
                
                while (sortedList.Count > totalCores)
                {
                    var min = sortedList[sortedList.Count - 1];
                    var subMin = sortedList[sortedList.Count - 2];
                    
                    sortedList.RemoveRange(sortedList.Count - 2, 2);
                    
                    //merge two smallest groups
                    subMin.AddRange(min);
                    

                    //start from before
                    int index = sortedList.Count - 1;
                    while (index >= 0 && sortedList[index].Count < subMin.Count)
                    {
                        index--;
                    }
                    
                    sortedList.Insert(index + 1, subMin);
                }

                return sortedList;
            }
        }


        /// <summary>
        /// Reblancing the group, this is a simple version where calculate the threshold [= txCount / totalCores] first,
        /// then fetch next biggest group in group result, merge smallest groups untill the transactions count in group reach threshold
        /// 
        /// Drawback of this approach: could produce some unbalance result in some cases:
        ///     Consider group result is [499, 497, 496, 495, 6, 3, 2, 2] with 3 cores
        ///     This func Will produce group in first step: [499, 499, 498, 498, 6], next step will be [1002, 499, 499] which is unbalanced
        /// </summary>
        /// <param name="totalCores"></param>
        /// <param name="unmergedGroups"></param>
        /// <returns></returns>
        /// <exception cref="InvalidParameterException"></exception>
        public List<List<Transaction>> ProcessWithCoreCount_MaxAddMins(int totalCores, List<List<Transaction>> unmergedGroups)
        {
            
            
            var sortedUnmergedGroups = unmergedGroups.OrderByDescending( a=> a.Count).ToList();
            
            if (sortedUnmergedGroups.Count == 0)
            {
                //this happens when all tx in the input list have failed.
                return new List<List<Transaction>>();
            }

            int transactionCount = sortedUnmergedGroups.SelectMany(a => a).Count();
            
            
            //TODO: group's count can be a little bit more that core count, for now it's 0, this value can latter make adjustable to deal with special uses
            int resGroupCount = totalCores + 0;
            int mergeThreshold = transactionCount / (resGroupCount);
            var res = new List<List<Transaction>>();

            int startIndex = 0, endIndex = sortedUnmergedGroups.Count - 1, totalCount = 0;

            while (startIndex <= endIndex)
            {
                var tempList = sortedUnmergedGroups.ElementAt(startIndex);
                while (tempList.Count + sortedUnmergedGroups.ElementAt(endIndex).Count <= mergeThreshold && startIndex < endIndex)
                {
                    tempList.AddRange(sortedUnmergedGroups.ElementAt(endIndex));
                    endIndex--;
                }
                res.Add(tempList);
                totalCount += tempList.Count;
                startIndex++;
            }

            //in case there is a bug 
            if (totalCount != transactionCount)
            {
                _logger.Fatal("There is a bug in the Grouper, get inconsist transaction count, some tx lost");
            }

            if (res.Count > resGroupCount)
            {
                var temp = res.OrderBy(a => a.Count).ToList();
                res.Clear();
                int index;
                var merge = new List<Transaction>();
                for (index = 0; index <= temp.Count - resGroupCount; index++)
                {
                    merge.AddRange(temp[index]);
                }
                res.Add(merge);
                for (; index < temp.Count; index++)
                {
                    res.Add(temp[index]);
                }
            }

            return res;
        }
    }
}
