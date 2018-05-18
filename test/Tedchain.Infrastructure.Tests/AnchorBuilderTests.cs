﻿// Copyright (c) 2010-2018 The Bitcoin developers
// Original code was distributed under the MIT software license.
// Copyright (c) 2014-2018 TEDLab Sciences Ltd
// Tedchain code distributed under the GPLv3 license, see COPYING file.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Tedchain.Infrastructure.Tests
{
    public class AnchorBuilderTests
    {
        private readonly ByteString[] binaryData =
            Enumerable.Range(0, 10).Select(index => new ByteString(Enumerable.Range(0, 32).Select(i => (byte)index))).ToArray();

        private readonly List<ByteString> transactions = new List<ByteString>();
        private readonly TestAnchorRecorder recorder = new TestAnchorRecorder();
        private readonly TestAnchorState state = new TestAnchorState();
        private readonly AnchorBuilder anchorBuilder;

        public AnchorBuilderTests()
        {
            this.anchorBuilder = new AnchorBuilder(
                new TestStorageEngine(transactions),
                recorder,
                state);
        }

        [Fact]
        public async Task RecordAnchor_ZeroTransaction()
        {
            LedgerAnchor anchor = await this.anchorBuilder.RecordAnchor();

            Assert.Null(anchor);
            Assert.Equal(0, recorder.Anchors.Count);
            Assert.Null(state.LastAnchor);
        }

        [Fact]
        public async Task RecordAnchor_OneTransaction()
        {
            ByteString hash = AddRecord("key1");
            ByteString expectedCumulativeHash = CombineHashes(new ByteString(new byte[32]), hash);

            LedgerAnchor anchor = await this.anchorBuilder.RecordAnchor();

            AssertAnchor(anchor, 1, hash, expectedCumulativeHash);
            Assert.Equal(1, recorder.Anchors.Count);
            AssertAnchor(recorder.Anchors[0], 1, hash, expectedCumulativeHash);
            AssertAnchor(state.LastAnchor, 1, hash, expectedCumulativeHash);
        }

        [Fact]
        public async Task RecordAnchor_TwoTransactions()
        {
            ByteString hash1 = AddRecord("key1");
            ByteString hash2 = AddRecord("key2");
            ByteString expectedCumulativeHash = CombineHashes(CombineHashes(new ByteString(new byte[32]), hash1), hash2);

            LedgerAnchor anchor = await this.anchorBuilder.RecordAnchor();

            AssertAnchor(anchor, 2, hash2, expectedCumulativeHash);
            Assert.Equal(1, recorder.Anchors.Count);
            AssertAnchor(recorder.Anchors[0], 2, hash2, expectedCumulativeHash);
            AssertAnchor(state.LastAnchor, 2, hash2, expectedCumulativeHash);
        }

        [Fact]
        public async Task RecordAnchor_OnePlusOneTransaction()
        {
            state.LastAnchor = new LedgerAnchor(binaryData[1], binaryData[2], 1);
            ByteString hash = AddRecord("key2");
            ByteString expectedCumulativeHash = CombineHashes(binaryData[2], hash);

            LedgerAnchor anchor = await this.anchorBuilder.RecordAnchor();

            AssertAnchor(anchor, 2, hash, expectedCumulativeHash);
            Assert.Equal(1, recorder.Anchors.Count);
            AssertAnchor(recorder.Anchors[0], 2, hash, expectedCumulativeHash);
            AssertAnchor(state.LastAnchor, 2, hash, expectedCumulativeHash);
        }

        [Fact]
        public async Task CreateAnchor_CannotRecord()
        {
            recorder.Enabled = false;
            LedgerAnchor anchor = await this.anchorBuilder.RecordAnchor();

            Assert.Null(anchor);
            Assert.Equal(0, recorder.Anchors.Count);
            Assert.Null(state.LastAnchor);
        }

        private void AssertAnchor(LedgerAnchor anchor, long transactionCount, ByteString position, ByteString fullStoreHash)
        {
            Assert.Equal(transactionCount, anchor.TransactionCount);
            Assert.Equal(position, anchor.Position);
            Assert.Equal(fullStoreHash, anchor.FullStoreHash);
        }

        private ByteString AddRecord(string key)
        {
            Mutation mutation = new Mutation(
                ByteString.Empty,
                new Record[] { new Record(new ByteString(Encoding.UTF8.GetBytes(key)), ByteString.Empty, ByteString.Empty) },
                ByteString.Empty);

            Transaction transaction = new Transaction(
                new ByteString(MessageSerializer.SerializeMutation(mutation)),
                new DateTime(),
                ByteString.Empty);

            this.transactions.Add(new ByteString(MessageSerializer.SerializeTransaction(transaction)));

            return new ByteString(MessageSerializer.ComputeHash(MessageSerializer.SerializeTransaction(transaction)));
        }

        private static ByteString CombineHashes(ByteString left, ByteString right)
        {
            using (SHA256 sha = SHA256.Create())
                return new ByteString(sha.ComputeHash(sha.ComputeHash(left.ToByteArray().Concat(right.ToByteArray()).ToArray())));
        }

        #region Mocks

        private class TestStorageEngine : IStorageEngine
        {
            private readonly List<ByteString> transactions;

            public TestStorageEngine(List<ByteString> transactions)
            {
                this.transactions = transactions;
            }

            public Task AddTransactions(IEnumerable<ByteString> transactions)
            {
                throw new NotImplementedException();
            }

            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public Task<ByteString> GetLastTransaction()
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<Record>> GetRecords(IEnumerable<ByteString> keys)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<ByteString>> GetTransactions(ByteString from) => Task.FromResult<IReadOnlyList<ByteString>>(transactions);

            public Task Initialize() => Task.FromResult(0);
        }

        public class TestAnchorRecorder : IAnchorRecorder
        {
            public IList<LedgerAnchor> Anchors { get; private set; } = new List<LedgerAnchor>();

            public bool Enabled { get; set; } = true;

            public Task<bool> CanRecordAnchor() => Task.FromResult(Enabled);

            public Task RecordAnchor(LedgerAnchor anchor)
            {
                Anchors.Add(anchor);
                return Task.FromResult(0);
            }
        }

        public class TestAnchorState : IAnchorState
        {
            public LedgerAnchor LastAnchor { get; set; } = null;

            public Task CommitAnchor(LedgerAnchor anchor)
            {
                this.LastAnchor = anchor;
                return Task.FromResult(0);
            }

            public Task<LedgerAnchor> GetLastAnchor() => Task.FromResult(LastAnchor);

            public Task Initialize() => Task.FromResult(0);
        }

        #endregion
    }
}
