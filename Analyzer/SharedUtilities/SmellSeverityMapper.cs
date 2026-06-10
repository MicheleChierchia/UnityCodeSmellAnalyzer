namespace AnalyzerUtilities
{
    public static class SmellSeverityMapper
    {
        public static SeverityLevel GetSeverity(string smellName)
        {
            switch (smellName)
            {
                // Livello 1 (Alta Criticità)
                case "Client-Side State Storage":
                case "Heavy Physics Computations":
                case "Heavy Physics Computation":
                case "Heavy Physics Computation ":
                case "Instantiate Destroy":
                case "Lack of optimization when drawing-rendering":
                case "Weak Temporization":
                    return SeverityLevel.High;

                // Livello 2 (Media Criticità)
                case "Lack of separation of concern":
                case "Incorrect Collision Mesh":
                case "Improper Collider":
                case "Mesh Collider Smells":
                case "Inefficient Data Transfer":
                case "String-based Object Searching":
                case "Find Methods":
                case "Mesh-based VFX":
                case "Dependency Between Objects":
                case "Poor State Design":
                case "Multiple animators for a single object":
                case "Anystate Smells":
                    return SeverityLevel.Medium;

                // Livello 3 (Bassa Criticità)
                case "Inspector-based Implicit Coupling":
                case "Static Coupling":
                case "Static Coupling Smells":
                case "Singleton Abuse":
                case "Singleton Pattern":
                case "Texture/Material Settings Smell":
                case "SubOptimal Expensive Lights":
                case "SubOptimal Expensive Lights with enable LightRealTime":
                case "Bloated Assets Smells":
                case "Direct Velocity Setting":
                case "Velocity Change":
                case "Check position or rotation":
                case "Too Many Key Frames":
                    return SeverityLevel.Low;

                default:
                    return SeverityLevel.Unknown;
            }
        }
    }
}
