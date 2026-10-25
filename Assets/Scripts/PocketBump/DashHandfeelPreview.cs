namespace PocketBump
{
    // Explicit development opt-in only. A Release player cannot activate provisional
    // simulation through this gate, even if a caller passes true.
    public static class DashHandfeelPreview
    {
#if UNITY_EDITOR
        public static bool Selected { get; set; }
#endif

        public static bool Allows(ArenaRulesProfile rules, bool requested)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return requested && rules != null && rules.IsProvisional &&
                rules.HandfeelPolicyVersion == 1 && rules.UsesRefinedHandfeel;
#else
            return false;
#endif
        }
    }
}
