using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Nightwire.Unity
{
    /// <summary>
    /// Server-authoritative fulfillment boundary. Implementations must verify the store transaction,
    /// apply entitlements/credits idempotently, and return true only after durable fulfillment.
    /// </summary>
    public interface INightwirePurchaseFulfillment
    {
        Task<bool> VerifyAndFulfillAsync(PendingOrder order);
        Task<bool> ReconcileRestoredPurchasesAsync(Orders orders);
    }

    /// <summary>
    /// Unity IAP 5.5 native-store integration. It never grants purchases locally: buying is disabled
    /// until a trusted fulfillment component is supplied by the production host.
    /// </summary>
    public sealed class NightwireIapService : MonoBehaviour
    {
        public const string EpisodePassId = "nightwire_episode_001_pass";
        public const string SignalCreditsSmallId = "nightwire_signal_bundle_small";
        public const string SignalCreditsMediumId = "nightwire_signal_bundle_medium";
        public const string SignalCreditsLargeId = "nightwire_signal_bundle_large";

        [SerializeField] private MonoBehaviour fulfillmentProvider;
        private PurchasingController controller;
        private CatalogProvider catalog;
        private string nativeStoreName;
        private bool initialized;
        private bool initializing;

        public event Action<string> StatusChanged;
        public event Action<List<Product>> ProductsUpdated;

        public bool IsReady { get; private set; }
        public bool CanPurchase => IsReady && fulfillmentProvider is INightwirePurchaseFulfillment;
        public IReadOnlyList<Product> Products { get; private set; } = new List<Product>();

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private async void Start()
        {
            await InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            if (initialized || initializing)
                return;

            initializing = true;
            nativeStoreName = UnityIAPServices.GetDefaultStore();
            try
            {
                catalog = new CatalogProvider();
                catalog.AddProduct(EpisodePassId, ProductType.NonConsumable);
                catalog.AddProduct(SignalCreditsSmallId, ProductType.Consumable);
                catalog.AddProduct(SignalCreditsMediumId, ProductType.Consumable);
                catalog.AddProduct(SignalCreditsLargeId, ProductType.Consumable);

                controller = new PurchasingController(nativeStoreName);
                controller.OnStoreDisconnected += OnStoreDisconnected;
                controller.OnProductsFetched += OnProductsFetched;
                controller.OnProductsFetchFailed += OnProductsFetchFailed;
                controller.OnPurchasePending += OnPurchasePending;
                controller.OnPurchasesFetched += OnPurchasesFetched;

                SetStatus("Connecting to the store…");
                await controller.Connect();
                catalog.FetchProducts(definitions =>
                    controller.FetchProductsWithNoRetries(definitions, nativeStoreName));
                initialized = true;
            }
            catch (Exception exception)
            {
                SetStatus("Store setup unavailable: " + exception.Message);
                Debug.LogException(exception, this);
            }
            finally
            {
                initializing = false;
            }
        }

        public bool TryPurchase(string productId)
        {
            if (!CanPurchase || string.IsNullOrWhiteSpace(productId))
            {
                SetStatus("Purchases are unavailable until secure purchase verification is configured.");
                return false;
            }

            bool listed = false;
            foreach (var product in Products)
            {
                if (product != null && product.availableToPurchase &&
                    string.Equals(product.definition.id, productId, StringComparison.Ordinal))
                {
                    listed = true;
                    break;
                }
            }

            if (!listed)
            {
                SetStatus("This store product is not currently available.");
                return false;
            }

            controller.PurchaseProduct(productId, nativeStoreName);
            return true;
        }

        public string GetLocalizedPrice(string productId)
        {
            foreach (var product in Products)
            {
                if (product != null && string.Equals(product.definition.id, productId, StringComparison.Ordinal))
                    return product.metadata.localizedPriceString;
            }
            return string.Empty;
        }

        private void OnProductsFetched(string storeName, List<Product> products)
        {
            if (!string.Equals(storeName, nativeStoreName, StringComparison.Ordinal))
                return;

            Products = products ?? new List<Product>();
            IsReady = true;
            ProductsUpdated?.Invoke(new List<Product>(Products));
            SetStatus("Store products loaded.");
            controller.FetchPurchases(storeName);
        }

        private async void OnPurchasesFetched(string storeName, Orders orders)
        {
            if (!string.Equals(storeName, nativeStoreName, StringComparison.Ordinal))
                return;

            var fulfillment = fulfillmentProvider as INightwirePurchaseFulfillment;
            if (fulfillment == null)
            {
                SetStatus("Existing purchases were found, but entitlement reconciliation is not configured.");
                return;
            }

            try
            {
                bool reconciled = await fulfillment.ReconcileRestoredPurchasesAsync(orders);
                SetStatus(reconciled
                    ? "Existing purchases were reconciled with the trusted account."
                    : "Existing purchase reconciliation is pending; no local entitlement was granted.");
            }
            catch (Exception exception)
            {
                SetStatus("Existing purchase reconciliation failed.");
                Debug.LogException(exception, this);
            }
        }

        private void OnProductsFetchFailed(string storeName, ProductFetchFailed failure)
        {
            SetStatus("Store products could not be loaded: " + failure.FailureReason);
        }

        private void OnStoreDisconnected(string storeName, StoreConnectionFailureDescription failure)
        {
            IsReady = false;
            SetStatus("Store disconnected: " + failure.message);
        }

        private async void OnPurchasePending(string storeName, PendingOrder order)
        {
            var fulfillment = fulfillmentProvider as INightwirePurchaseFulfillment;
            if (fulfillment == null)
            {
                SetStatus("Purchase received but not fulfilled: secure verification is not configured.");
                return;
            }

            try
            {
                SetStatus("Verifying purchase securely…");
                bool fulfilled = await fulfillment.VerifyAndFulfillAsync(order);
                if (!fulfilled)
                {
                    SetStatus("The transaction remains pending. No items were granted.");
                    return;
                }

                controller.ConfirmPurchase(order);
                SetStatus("Purchase verified and fulfilled.");
            }
            catch (Exception exception)
            {
                SetStatus("Purchase verification failed. The transaction remains pending.");
                Debug.LogException(exception, this);
            }
        }

        private void SetStatus(string message)
        {
            StatusChanged?.Invoke(message);
            Debug.Log("NIGHTWIRE IAP: " + message, this);
        }

        private void OnDestroy()
        {
            if (controller == null)
                return;

            controller.OnStoreDisconnected -= OnStoreDisconnected;
            controller.OnProductsFetched -= OnProductsFetched;
            controller.OnProductsFetchFailed -= OnProductsFetchFailed;
            controller.OnPurchasePending -= OnPurchasePending;
            controller.OnPurchasesFetched -= OnPurchasesFetched;
        }
    }
}
