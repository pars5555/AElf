﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.Concurrency;
using AElf.Kernel.Concurrency.Metadata;

namespace AElf.Api.CSharp
{
    public class AttributeException : Exception
    {
        public AttributeException(string message):base(message)
        {
            
        }
    }
    public class Map
    {
        private string _name;
        private int _count;
        private List<Hash> _keySet;

        [SmartContractFieldData("Count", DataAccessMode.ReadWriteAccountSharing)]
        public int Count
        {
            [SmartContractFunction("Count", new string[]{}, new []{"Count"})]
            get { return _count; }
        }

        /// <summary>
        /// For now we don't consider Iteratable map, cause this will futher degrade concurrency degree. However this can be surported by grouping alg, see in documentation.
        /// </summary>
        //[SmartContractFieldData("KeyEnumerator", DataAccessMode.ReadWriteAccountSharing)]
        /*public List<Hash>.Enumerator KeyEnumerator
        {
            [SmartContractFunction("KeyEnumerator", new []{"KeyEnumerator"})]
            get { return _keySet.GetEnumerator(); }
        }*/

        public Map(string name)
        {
            _name = name;
            _count = 0;
        }
        
        public async Task SetValueAsync(Hash keyHash, byte[] value)
        {
            await Api.GetDataProvider(_name).SetAsync(keyHash, value);
        }

        public async Task<byte[]> GetValue(Hash keyHash)
        {
            return await Api.GetDataProvider(_name).GetAsync(keyHash);
        }
    }
}