// Copyright (c) 2011-2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.
namespace SiliconStudio.Core.Transactions
{
    /// <summary>
    /// A enum listing the possible reasons for transactions to be discarded from an <see cref="ITransactionStack"/>.
    /// </summary>
    public enum DiscardReason
    {
        /// <summary>
        /// Transactions have been discarded because the stack is full.
        /// </summary>
        StackFull,
        /// <summary>
        /// Transactions have been discarded because the top of the stack has been purged.
        /// </summary>
        StackPurged,
    }
}
