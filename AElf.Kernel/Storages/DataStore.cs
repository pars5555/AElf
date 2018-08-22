﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.Database;
using System.Linq;
using AElf.Kernel.Types;
using Google.Protobuf;
using Org.BouncyCastle.Asn1.X509;

namespace AElf.Kernel.Storages
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public sealed class DataStore : IDataStore
    {
        private readonly IKeyValueDatabase _keyValueDatabase;

        public DataStore(IKeyValueDatabase keyValueDatabase)
        {
            _keyValueDatabase = keyValueDatabase;
        }

        public async Task InsertAsync<T>(Hash pointerHash, T obj) where T : IMessage
        {
            try
            {
                if (pointerHash == null)
                {
                    throw new Exception("Point hash cannot be null.");
                }

                if (obj == null)
                {
                    throw new Exception("Cannot insert null value.");
                }
                
                if (!Enum.TryParse<Types>(typeof(T).Name, out var typeIndex))
                {
                    throw new Exception($"Not Supported Data Type, {typeof(T).Name}.");
                }
                var key = pointerHash.GetKeyString((uint)typeIndex);
                await _keyValueDatabase.SetAsync(key, obj.ToByteArray());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<T> GetAsync<T>(Hash pointerHash) where T : IMessage, new()
        {
            try
            {
                if (pointerHash == null)
                {
                    throw new Exception("Pointer hash cannot be null.");
                }
                
                if (!Enum.TryParse<Types>(typeof(T).Name, out var typeIndex))
                {
                    throw new Exception($"Not Supported Data Type, {typeof(T).Name}.");
                }
                
                var key = pointerHash.GetKeyString((uint)typeIndex);
                var res = await _keyValueDatabase.GetAsync(key);
                return  res == null ? default(T): res.Deserialize<T>();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<bool> PipelineSetDataAsync(Dictionary<Hash, byte[]> pipelineSet)
        {
            try
            {
                foreach (var kv in pipelineSet)
                {
                    Console.WriteLine($"Set: {kv.Key.ToHex()} - {kv.Value.Length}");
                }
                return await _keyValueDatabase.PipelineSetAsync(
                    pipelineSet.ToDictionary(kv => kv.Key.ToHex(), kv => kv.Value));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
        }

        public async Task RemoveAsync<T>(Hash pointerHash) where T : IMessage
        {
            try
            {
                if (pointerHash == null)
                {
                    throw new Exception("Pointer hash cannot be null.");
                }
                if (!Enum.TryParse<Types>(typeof(T).Name, out var typeIndex))
                {
                    throw new Exception($"Not Supported Data Type, {typeof(T).Name}.");
                }
                var key = pointerHash.GetKeyString((uint)typeIndex);
                await _keyValueDatabase.RemoveAsync(key);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}