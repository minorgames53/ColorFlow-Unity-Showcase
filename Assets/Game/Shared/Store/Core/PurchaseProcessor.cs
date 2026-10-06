using System;
using System.Collections.Generic;
using Game.Shared.Save;
using UnityEngine;

namespace Game.Shared.Store
{
    public enum PurchaseProcessStatus
    {
        Succeeded = 0,
        Duplicate = 1,
        Failed = 2,
        GameplayFulfillmentPending = 3
    }

    public readonly struct PurchaseProcessResult
    {
        public PurchaseProcessResult(
            PurchaseProcessStatus status,
            bool gameplayActionApplied,
            string error)
        {
            Status = status;
            GameplayActionApplied = gameplayActionApplied;
            Error = error ?? string.Empty;
        }

        public PurchaseProcessStatus Status { get; }
        public bool GameplayActionApplied { get; }
        public string Error { get; }
    }

    public sealed class PurchaseProcessor
    {
        private readonly SaveManager saveManager;
        private readonly StoreRewardProcessor rewardProcessor;
        private readonly HashSet<string> gameplayAppliedThisRuntime =
            new HashSet<string>(StringComparer.Ordinal);

        public PurchaseProcessor(
            SaveManager saveManager,
            StoreRewardProcessor rewardProcessor)
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            this.rewardProcessor = rewardProcessor ??
                throw new ArgumentNullException(nameof(rewardProcessor));
        }

        public PurchaseProcessResult Process(
            StorePurchase purchase,
            StoreProductDefinition product,
            StoreRewardContext context)
        {
            if (purchase == null || string.IsNullOrWhiteSpace(purchase.TransactionId))
            {
                return Failed("Purchase has no transaction ID.");
            }

            try
            {
                if (saveManager.IsWriteBlocked)
                {
                    return Failed("Save writes are blocked; purchase was left pending.");
                }

                bool hasGameplayReward = HasGameplayReward(product);
                if (saveManager.IsIapTransactionProcessed(purchase.TransactionId))
                {
                    saveManager.MarkIapTransactionUnconfirmed(
                        purchase.TransactionId,
                        product.ProductId);
                    if (saveManager.IsDirty && !saveManager.TrySave())
                    {
                        return Failed("The processed transaction state could not be persisted.");
                    }

                    if (hasGameplayReward)
                    {
                        return ProcessExistingGameplayFulfillment(purchase, product);
                    }

                    Debug.Log($"[Store] Duplicate transaction reward skipped: {purchase.TransactionId}");
                    return new PurchaseProcessResult(
                        PurchaseProcessStatus.Duplicate,
                        false,
                        string.Empty);
                }

                string saveSnapshot = saveManager.CaptureRuntimeSnapshot();
                bool wasDirty = saveManager.IsDirty;

                // Validate the captured session before mutating persistent state. If it becomes
                // stale, the paid Continue is converted to a durable recovery credit.
                bool gameplayActionApplicable = !hasGameplayReward ||
                    (context.HasFailedSession &&
                     StoreGameplayActionRegistry.CanContinueFailedSession(
                         context.FailedSessionToken));

                // Rewards and the transaction marker are written in the same atomic save payload.
                // The store purchase remains pending unless that write succeeds.
                try
                {
                    rewardProcessor.ApplyPersistentRewards(product);
                    saveManager.MarkIapTransactionUnconfirmed(
                        purchase.TransactionId,
                        product.ProductId);
                    saveManager.MarkIapTransactionProcessed(purchase.TransactionId);
                    if (hasGameplayReward)
                    {
                        saveManager.SetIapGameplayFulfillment(
                            purchase.TransactionId,
                            product.ProductId,
                            context.HasFailedSession
                                ? context.FailedSessionToken
                                : string.Empty,
                            false);
                    }

                    if (!saveManager.TrySave())
                    {
                        saveManager.RestoreRuntimeSnapshot(saveSnapshot, wasDirty);
                        return Failed("Rewards could not be persisted; purchase was left pending.");
                    }
                }
                catch
                {
                    saveManager.RestoreRuntimeSnapshot(saveSnapshot, wasDirty);
                    throw;
                }

                if (hasGameplayReward)
                {
                    if (!gameplayActionApplicable)
                    {
                        return PersistRecoveryContinue(purchase, product);
                    }

                    return TryCompleteGameplayFulfillment(
                        purchase,
                        product,
                        context.FailedSessionToken,
                        false);
                }

                return new PurchaseProcessResult(
                    PurchaseProcessStatus.Succeeded,
                    false,
                    string.Empty);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[Store] Reward processing failed for '{product?.ProductId}': {exception}");
                return Failed(exception.Message);
            }
        }

        private PurchaseProcessResult ProcessExistingGameplayFulfillment(
            StorePurchase purchase,
            StoreProductDefinition product)
        {
            if (!saveManager.TryGetIapGameplayFulfillment(
                    purchase.TransactionId,
                    out string recordedProductId,
                    out string failedSessionToken,
                    out bool gameplayRewardApplied))
            {
                // Saves produced before fulfillment tracking only prove that persistent rewards
                // were applied. Convert their missing Continue into one transaction-backed
                // recovery credit instead of attaching the order to an arbitrary fail session.
                return PersistRecoveryContinue(purchase, product);
            }

            if (!string.Equals(recordedProductId, product.ProductId, StringComparison.Ordinal))
            {
                return Failed(
                    "The transaction gameplay fulfillment belongs to a different product.");
            }

            if (gameplayRewardApplied)
            {
                Debug.Log(
                    $"[Store] Duplicate transaction reward skipped; gameplay fulfillment is complete: {purchase.TransactionId}");
                return new PurchaseProcessResult(
                    PurchaseProcessStatus.Duplicate,
                    false,
                    string.Empty);
            }

            return TryCompleteGameplayFulfillment(
                purchase,
                product,
                failedSessionToken,
                gameplayAppliedThisRuntime.Contains(purchase.TransactionId));
        }

        private PurchaseProcessResult TryCompleteGameplayFulfillment(
            StorePurchase purchase,
            StoreProductDefinition product,
            string failedSessionToken,
            bool actionAlreadyAppliedThisRuntime)
        {
            try
            {
                bool gameplayActionApplied = actionAlreadyAppliedThisRuntime;
                if (!gameplayActionApplied)
                {
                    if (string.IsNullOrEmpty(failedSessionToken) ||
                        !StoreGameplayActionRegistry.CanContinueFailedSession(
                            failedSessionToken))
                    {
                        return PersistRecoveryContinue(purchase, product);
                    }

                    StoreRewardContext storedContext =
                        new StoreRewardContext(true, failedSessionToken);
                    if (!rewardProcessor.ApplyGameplayRewards(product, storedContext))
                    {
                        return PersistRecoveryContinue(purchase, product);
                    }

                    gameplayAppliedThisRuntime.Add(purchase.TransactionId);
                    gameplayActionApplied = true;
                }

                saveManager.SetIapGameplayFulfillment(
                    purchase.TransactionId,
                    product.ProductId,
                    failedSessionToken,
                    true);
                if (!saveManager.TrySave())
                {
                    return GameplayPending(
                        "Continue was applied, but its fulfillment marker could not be persisted.");
                }

                gameplayAppliedThisRuntime.Remove(purchase.TransactionId);
                return new PurchaseProcessResult(
                    PurchaseProcessStatus.Succeeded,
                    gameplayActionApplied,
                    string.Empty);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[Store] Gameplay fulfillment failed for '{product.ProductId}': {exception}");
                return GameplayPending(exception.Message);
            }
        }

        private PurchaseProcessResult PersistRecoveryContinue(
            StorePurchase purchase,
            StoreProductDefinition product)
        {
            string saveSnapshot = saveManager.CaptureRuntimeSnapshot();
            bool wasDirty = saveManager.IsDirty;

            try
            {
                if (!saveManager.GrantFailOfferRecoveryContinueCredit())
                {
                    return GameplayPending(
                        "The Fail Offer recovery Continue credit could not be created safely.");
                }

                // Credit creation and transaction gameplay fulfillment are one durable payload.
                // A redelivery sees gameplayRewardApplied and cannot create a second credit.
                saveManager.SetIapGameplayFulfillment(
                    purchase.TransactionId,
                    product.ProductId,
                    string.Empty,
                    true);
                if (!saveManager.TrySave())
                {
                    saveManager.RestoreRuntimeSnapshot(saveSnapshot, wasDirty);
                    return GameplayPending(
                        "The Fail Offer recovery Continue credit could not be persisted.");
                }

                return new PurchaseProcessResult(
                    PurchaseProcessStatus.Succeeded,
                    false,
                    string.Empty);
            }
            catch (Exception exception)
            {
                saveManager.RestoreRuntimeSnapshot(saveSnapshot, wasDirty);
                Debug.LogError(
                    $"[Store] Recovery Continue persistence failed for transaction '{purchase.TransactionId}': {exception}");
                return GameplayPending(exception.Message);
            }
        }

        private static bool HasGameplayReward(StoreProductDefinition product)
        {
            for (int i = 0; i < product.Rewards.Count; i++)
            {
                if (product.Rewards[i].Type == StoreRewardType.ContinueLevel)
                {
                    return true;
                }
            }

            return false;
        }

        public bool ApplyRestoredNoAdsEntitlement()
        {
            try
            {
                if (saveManager.HasNoAds)
                {
                    return !saveManager.IsDirty || saveManager.TrySave();
                }

                string saveSnapshot = saveManager.CaptureRuntimeSnapshot();
                bool wasDirty = saveManager.IsDirty;
                saveManager.SetHasNoAds(true);
                if (saveManager.TrySave())
                {
                    return true;
                }

                saveManager.RestoreRuntimeSnapshot(saveSnapshot, wasDirty);
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Store] No Ads restore failed: {exception}");
                return false;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool DebugGrant(
            StoreProductDefinition product,
            StoreRewardContext context,
            bool includeGameplayActions)
        {
            try
            {
                rewardProcessor.ApplyPersistentRewards(product);
                if (!saveManager.TrySave())
                {
                    return false;
                }

                return !includeGameplayActions ||
                       rewardProcessor.ApplyGameplayRewards(product, context);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Store] Debug reward grant failed: {exception}");
                return false;
            }
        }
#endif

        private static PurchaseProcessResult Failed(string error)
        {
            return new PurchaseProcessResult(
                PurchaseProcessStatus.Failed,
                false,
                error);
        }

        private static PurchaseProcessResult GameplayPending(string error)
        {
            return new PurchaseProcessResult(
                PurchaseProcessStatus.GameplayFulfillmentPending,
                false,
                error);
        }
    }
}
