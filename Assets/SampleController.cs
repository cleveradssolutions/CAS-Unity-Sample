using UnityEngine;
using UnityEngine.UI;
using CAS;
using CAS.UserConsent;
using System.Collections.Generic;

public class SampleController : MonoBehaviour
{
    public ConsentStatus userConsent;
    public CCPAStatus userCCPAStatus;
    public Text versionText;
    public Text bannerStatus;
    public Text interstitialStatus;
    public Text rewardedStatus;
    public IMediationManager manager;

    public Text appReturnStatus;
    public Text appReturnButtonText;

    private bool isAppReturnEnable = false;

    private IAdView bannerView;

    public void Start()
    {
        // -- Privacy Laws (Optional):
        MobileAds.settings.userConsent = userConsent;
        MobileAds.settings.userCCPAStatus = userCCPAStatus;

        // -- Configuring CAS SDK (Optional):
        MobileAds.settings.isExecuteEventsOnUnityThread = true;

        // -- Create manager:
        manager = MobileAds.BuildManager().Initialize();

        // -- Subscribe to CAS events:
        manager.OnRewardedAdCompleted += RewardedSuccessful;
        // Any other callbacks from IMediationManager

        // -- Get native CAS SDK version
        versionText.text = MobileAds.GetSDKVersion();

        InvokeRepeating( "OnRefreshStatus", 1.0f, 1.0f );

        bannerView = manager.GetAdView( AdSize.Banner );
        ShowBanner();
    }
    
    public void ShowBanner()
    {
        bannerView.SetActive( true );
    }

    public void HideBanner()
    {
        bannerView.SetActive( false );
    }

    public void SetBannerPosition( int positionEnum )
    {
        bannerView.position = ( AdPosition )positionEnum;
    }

    public void SetBannerSize( int sizeID )
    {
        bannerView = manager.GetAdView( ( AdSize )sizeID );
        ShowBanner(); 
    }

    public void ShowInterstitial()
    {
        if (manager.IsReadyAd( AdType.Interstitial ))
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

    public void ChangeAppReturnState()
    {
        if (isAppReturnEnable)
        {
            appReturnStatus.text = "DISABLED";
            appReturnButtonText.text = "ENABLE";
            manager.SetAppReturnAdsEnabled( false );
            isAppReturnEnable = false;
        }
        else
        {
            appReturnStatus.text = "ENABLED";
            appReturnButtonText.text = "DISABLE";
            manager.SetAppReturnAdsEnabled( true );
            isAppReturnEnable = true;
        }
    }

    private void OnRefreshStatus()
    {
        bannerStatus.text = manager.IsReadyAd( AdType.Banner ) ? "Ready" : "Loading";
        interstitialStatus.text = manager.IsReadyAd( AdType.Interstitial ) ? "Ready" : "Loading";
        rewardedStatus.text = manager.IsReadyAd( AdType.Rewarded ) ? "Ready" : "Loading";
    }

    private void RewardedSuccessful()
    {
        Debug.Log( "Rewarded Successful" );
    }
}
