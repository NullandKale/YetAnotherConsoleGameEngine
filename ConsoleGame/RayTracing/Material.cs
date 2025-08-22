namespace ConsoleGame.RayTracing
{
    public struct Material
    {
        public Vec3 Albedo;
        public double Specular;
        public double Reflectivity;
        public Vec3 Emission;

        // Added for transparent materials
        public double Transparency;              // 0 = opaque, 1 = fully transmissive
        public double IndexOfRefraction;         // Typical glass ~1.5
        public Vec3 TransmissionColor;           // Tint for transmitted light

        public Material(Vec3 albedo, double specular, double reflectivity, Vec3 emission)
        {
            Albedo = albedo;
            Specular = specular;
            Reflectivity = reflectivity;
            Emission = emission;

            Transparency = 0.0;
            IndexOfRefraction = 1.5;
            TransmissionColor = new Vec3(1.0, 1.0, 1.0);
        }

        public Material(Vec3 albedo, double specular, double reflectivity, Vec3 emission, double transparency, double indexOfRefraction, Vec3 transmissionColor)
        {
            Albedo = albedo;
            Specular = specular;
            Reflectivity = reflectivity;
            Emission = emission;
            Transparency = transparency;
            IndexOfRefraction = indexOfRefraction;
            TransmissionColor = transmissionColor;
        }
    }
}
