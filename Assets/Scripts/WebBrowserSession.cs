namespace Wyvernland
{
    internal static class WebBrowserSession
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void WyvernlandReturnToWebsite();
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void WyvernlandSetLoading(int loading);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int WyvernlandSelectedMap();
#endif

        internal static void ReturnToWebsite()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WyvernlandReturnToWebsite();
#endif
        }

        internal static void SetLoading(bool loading)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WyvernlandSetLoading(loading ? 1 : 0);
#endif
        }
        internal static int SelectedMap {
            get {
#if UNITY_WEBGL && !UNITY_EDITOR
                return WyvernlandSelectedMap();
#else
                return -1;
#endif
            }
        }
    }
}
