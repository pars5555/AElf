using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Kernel.Blockchain.Application;

namespace AElf.OS.Network.Extensions
{
    public static class BlockchainServiceExtensions
    {
        public static async Task<BlockWithTransactions> GetBlockWithTransactionsByHash(this IBlockchainService blockchainService, Hash blockHash)
        {
            var block = await blockchainService.GetBlockByHashAsync(blockHash);

            if (block == null)
                return null;
            
            var transactions = await blockchainService.GetTransactionsAsync(block.TransactionHashList);
            
            return new BlockWithTransactions
            {
                Header = block.Header, 
                Transactions = { transactions }
            };
        }
        
        public static async Task<List<BlockWithTransactions>> GetBlocksWithTransactions(this IBlockchainService blockchainService,
            Hash firstHash, int count)
        {
            var chain = await blockchainService.GetChainAsync();
            var blockHashes = await blockchainService.GetBlockHashesAsync(chain, firstHash, count, chain.BestChainHash);
            
            var list = blockHashes
                .Select(async blockHash => await blockchainService.GetBlockWithTransactionsByHash(blockHash));

            return (await Task.WhenAll(list)).ToList();
        }
    }
}