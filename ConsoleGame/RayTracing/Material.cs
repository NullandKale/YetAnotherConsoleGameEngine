namespace ConsoleGame.RayTracing
{
    using ConsoleGame.Renderer;

    public struct Material
    {
        public Vec3 Albedo;
        public double Specular;
        public double Reflectivity;
        public Vec3 Emission;

        public double Transparency;
        public double IndexOfRefraction;
        public Vec3 TransmissionColor;

        public Texture DiffuseTexture;
        public double TextureWeight;
        public double UVScale;

        public Material(Vec3 albedo, double specular, double reflectivity, Vec3 emission)
        {
            Albedo = albedo;
            Specular = specular;
            Reflectivity = reflectivity;
            Emission = emission;
            Transparency = 0.0;
            IndexOfRefraction = 1.5;
            TransmissionColor = new Vec3(1.0, 1.0, 1.0);
            DiffuseTexture = null;
            TextureWeight = 1.0;
            UVScale = 1.0;
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
            DiffuseTexture = null;
            TextureWeight = 1.0;
            UVScale = 1.0;
        }

        public Material(Vec3 albedo, double specular, double reflectivity, Vec3 emission, Texture diffuseTexture, double textureWeight, double uvScale, double transparency, double indexOfRefraction, Vec3 transmissionColor)
        {
            Albedo = albedo;
            Specular = specular;
            Reflectivity = reflectivity;
            Emission = emission;
            DiffuseTexture = diffuseTexture;
            TextureWeight = textureWeight;
            UVScale = uvScale;
            Transparency = transparency;
            IndexOfRefraction = indexOfRefraction;
            TransmissionColor = transmissionColor;
        }
    }
}