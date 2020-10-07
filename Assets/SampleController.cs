using UnityEngine;
using UnityEngine.UI;
using CAS;

public class SampleController : MonoBehaviour
{
    public ConsentStatus userConsent;
    public CCPAStatus userCCPAStatus;
    public Text versionText;
    public Text bannerStatus;
    public Text interstitialStatus;
    public Text rewardedStatus;
    public IMediationManager manager;

    public void Start()
    {
        // -- Privacy Laws (Optional):
        MobileAds.settings.userConsent = userConsent;
        MobileAds.settings.userCCPAStatus = userCCPAStatus;

        // -- Configuring CAS SDK (Optional):
        MobileAds.settings.isExecuteEventsOnUnityThread = true;
        MobileAds.settings.allowInterstitialAdsWhenVideoCostAreLower = true;

        // -- Create manager:
        manager = MobileAds.InitializeFromResources();
        // OR same 
        //manager = MobileAds.Initialize(managerID, AdFlags.Everything, true);

        // -- Configuring Banner Ad
        // Init Banner Size can be selected in Assets/CleverAdsSolutions/Settings
        //manager.bannerSize = AdSize.AdaptiveBanner; 
        manager.bannerPosition = AdPosition.BottomCenter;

        // -- Subscribe to CAS events:
        manager.OnRewardedAdCompleted += RewardedSuccessful;
        // Any other callbacks from IMediationManager

        // -- Get native CAS SDK version
        versionText.text = MobileAds.GetSDKVersion();

        InvokeRepeating( "OnRefreshStatus", 1.0f, 1.0f );
    }

    public void ShowBanner()
    {
        manager.ShowAd( AdType.Banner );
    }

    public void HideBanner()
    {
        manager.HideBanner();
    }

    public void SetBannerPosition(int positionEnum)
    {
        manager.bannerPosition = ( AdPosition )positionEnum;
    }

    public void SetBannerSize(int sizeID)
    {
        manager.bannerSize = ( AdSize )sizeID;
    }

    public void ShowInterstitial()
    {
        if(manager.IsReadyAd( AdType.Interstitial ))
            manager.ShowAd( AdType.Interstitial );
        else
            Debug.LogError( "Interstitial Ad are not ready. Please try again later." );
    }

    public void ShowRewarded()
    {
        if (manager.IsReadyAd( AdType.Rewarded ))
            manager.ShowAd( AdType.Rewarded );
        else
            Debug.LogError( "Rewarded Video Ad are not ready. Please try again later." );
    }

    private void OnRefreshStatus()
    {
        bannerStatus.text = manager.IsReadyAd( AdType.Banner) ? "Ready" : "Loading";
        interstitialStatus.text = manager.IsReadyAd( AdType.Interstitial) ? "Ready" : "Loading";
        rewardedStatus.text = manager.IsReadyAd( AdType.Rewarded) ? "Ready" : "Loading";
    }

    private void RewardedSuccessful()
    {
        Debug.Log("Rewarded Successful");
    }
}
