﻿// Copyright (c) 2010-2018 The Bitcoin developers
// Original code was distributed under the MIT software license.
// Copyright (c) 2014-2018 TEDLab Sciences Ltd
// Tedchain code distributed under the GPLv3 license, see COPYING file.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tedchain.Infrastructure;

namespace Tedchain.Validation.PermissionBased
{
    public class PermissionBasedValidator : IMutationValidator
    {
        private readonly IList<IPermissionsProvider> permissions;

        public PermissionBasedValidator(IList<IPermissionsProvider> permissions)
        {
            this.permissions = permissions;
        }

        public async Task<IList<Mutation>> Validate(ParsedMutation mutation, IReadOnlyList<SignatureEvidence> authentication, IReadOnlyDictionary<AccountKey, AccountStatus> accounts)
        {
            await ValidateAccountMutations(mutation.AccountMutations, accounts, authentication);
            await ValidateDataMutations(mutation.DataRecords, authentication);

            return new Mutation[0];
        }

        private async Task ValidateAccountMutations(
            IReadOnlyList<AccountStatus> accountMutations,
            IReadOnlyDictionary<AccountKey, AccountStatus> accounts,
            IReadOnlyList<SignatureEvidence> signedAddresses)
        {
            foreach (AccountStatus mutation in accountMutations)
            {
                PermissionSet accountPermissions = await GetPermissions(signedAddresses, mutation.AccountKey.Account, mutation.AccountKey.Asset.FullPath);

                AccountStatus previousStatus = accounts[mutation.AccountKey];

                if (mutation.Version.Equals(ByteString.Empty))
                {
                    if (accountPermissions.AccountCreate != Access.Permit)
                        throw new TransactionInvalidException("AccountCreationUnauthorized");
                }
                else
                {
                    if (accountPermissions.AccountModify != Access.Permit)
                        throw new TransactionInvalidException("AccountModificationUnauthorized");
                }

                if (mutation.Balance <= previousStatus.Balance && accountPermissions.AccountNegative != Access.Permit)
                {
                    // Decreasing the balance
                    if (mutation.Balance >= 0)
                    {
                        // Spending existing funds
                        if (accountPermissions.AccountSpend != Access.Permit)
                            throw new TransactionInvalidException("CannotSpendFromAccount");
                    }
                    else
                    {
                        // Spending non-existing funds
                        throw new TransactionInvalidException("CannotIssueAsset");
                    }
                }
            }
        }

        private async Task ValidateDataMutations(
            IReadOnlyList<KeyValuePair<RecordKey, ByteString>> aliases,
            IReadOnlyList<SignatureEvidence> signedAddresses)
        {
            foreach (KeyValuePair<RecordKey, ByteString> alias in aliases)
            {
                PermissionSet dataRecordPermissions = await GetPermissions(signedAddresses, alias.Key.Path, alias.Key.Name);

                if (dataRecordPermissions.DataModify != Access.Permit)
                    throw new TransactionInvalidException("CannotModifyData");
            }
        }

        private async Task<PermissionSet> GetPermissions(IReadOnlyList<SignatureEvidence> signedAddresses, LedgerPath path, string recordName)
        {
            PermissionSet accumulativePermissions = PermissionSet.DenyAll;

            for (int i = 0; i <= path.Segments.Count; i++)
            {
                bool recursiveOnly = i != path.Segments.Count;
                LedgerPath currentPath = LedgerPath.FromSegments(path.Segments.Take(i).ToArray());
                PermissionSet[] permissions = await Task.WhenAll(this.permissions.Select(item => item.GetPermissions(signedAddresses, currentPath, recursiveOnly, recordName)));

                PermissionSet currentLevelPermissions = permissions
                    .Aggregate(PermissionSet.Unset, (previous, current) => previous.Add(current));

                accumulativePermissions = accumulativePermissions.AddLevel(currentLevelPermissions);
            }

            return accumulativePermissions;
        }
    }
}
