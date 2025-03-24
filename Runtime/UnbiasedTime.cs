using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace com.ktgame.datetime
{
    public class UnbiasedTime
    {
        private long _timeOffset = 0;

        public long TimeOffset => _timeOffset;

        public DateTime Now => DateTime.Now.AddSeconds(-1.0f * _timeOffset);

        public void UpdateTimeOffset()
        {
#if UNITY_ANDROID
            UpdateTimeOffsetAndroid();
#elif UNITY_IPHONE
			UpdateTimeOffsetIOS();
#endif
        }

        public bool IsUsingSystemTime()
        {
#if UNITY_ANDROID
            return UsingSystemTimeAndroid();
#elif UNITY_IPHONE
			return UsingSystemTimeIOS();
#else
			return true;
#endif
        }

        public void SessionStart()
        {
#if UNITY_ANDROID
            StartAndroid();
#elif UNITY_IPHONE
			StartIOS();
#endif
        }

        public void SessionEnd()
        {
#if UNITY_ANDROID
            EndAndroid();
#elif UNITY_IPHONE
			EndIOS();
#endif
        }

#if UNITY_IPHONE
        [DllImport("__Internal")]
        private static extern void _vtcOnSessionStart();

        [DllImport("__Internal")]
        private static extern void _vtcOnSessionEnd();

        [DllImport("__Internal")]
        private static extern int _vtcTimestampOffset();

        [DllImport("__Internal")]
        private static extern int _vtcUsingSystemTime();

        private void UpdateTimeOffsetIOS()
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer) return;
            _timeOffset = _vtcTimestampOffset();
        }

        private void StartIOS()
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer) return;
            _vtcOnSessionStart();
            _timeOffset = _vtcTimestampOffset();
        }

        private void EndIOS()
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer) return;
            _vtcOnSessionEnd();
        }

        private bool UsingSystemTimeIOS()
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer) return true;
            return _vtcUsingSystemTime() != 0;
        }
#endif

#if UNITY_ANDROID
        private void UpdateTimeOffsetAndroid()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var unbiasedTimeClass = new AndroidJavaClass("com.vasilij.unbiasedtime.UnbiasedTime");
            var playerActivityContext = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
            if (playerActivityContext != null)
            {
                _timeOffset = unbiasedTimeClass.CallStatic<long>("vtcTimestampOffset", playerActivityContext);
            }
        }

        private void StartAndroid()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var unbiasedTimeClass = new AndroidJavaClass("com.vasilij.unbiasedtime.UnbiasedTime");
            var playerActivityContext = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
            if (playerActivityContext != null)
            {
                unbiasedTimeClass.CallStatic("vtcOnSessionStart", playerActivityContext);
                _timeOffset = unbiasedTimeClass.CallStatic<long>("vtcTimestampOffset");
            }
        }

        private void EndAndroid()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var unbiasedTimeClass = new AndroidJavaClass("com.vasilij.unbiasedtime.UnbiasedTime");
            var playerActivityContext = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
            if (playerActivityContext != null)
            {
                unbiasedTimeClass.CallStatic("vtcOnSessionEnd", playerActivityContext);
            }
        }

        private bool UsingSystemTimeAndroid()
        {
            if (Application.platform != RuntimePlatform.Android) return true;
            using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var unbiasedTimeClass = new AndroidJavaClass("com.vasilij.unbiasedtime.UnbiasedTime");
            var playerActivityContext = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
            if (playerActivityContext != null)
            {
                return unbiasedTimeClass.CallStatic<bool>("vtcUsingDeviceTime");
            }

            return true;
        }
#endif
    }
}