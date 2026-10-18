namespace System.engine.RH
{
    public static class LogoutHandler
    {
        public static void HandleRequest()
        {
            RememberMe.RevokeCurrent();
            SessionStore.Abandon();
            // Send the user to the admin slug root; now unauthenticated, the
            // guard there renders the login page. There is no /login route.
            ApiHelper.Redirect("/" + AdminSlug.Current);
        }
    }
}
