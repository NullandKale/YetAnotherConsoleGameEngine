using ConsoleGame.Components;
using ConsoleGame.Renderer;
using System.Collections.Generic;

namespace ConsoleGame.Entities
{
    public class BaseEntity
    {
        public int X;
        public int Y;
        public Chexel Chexel;
        private List<BaseComponent> components;

        public BaseEntity(int x, int y, Chexel chexel)
        {
            X = x;
            Y = y;
            Chexel = chexel;
            components = new List<BaseComponent>();
        }

        public void AddComponent(BaseComponent component)
        {
            component.Parent = this; // Set the parent of the component
            components.Add(component);
        }

        public void RemoveComponent(BaseComponent component)
        {
            component.Parent = null; // Clear the parent reference
            components.Remove(component);
        }

        public void Update(double deltaTime)
        {
            for (int i = 0; i < components.Count; i++)
            {
                BaseComponent component = components[i];
                component.Update(deltaTime);
            }
        }

        public void HandleMouse(TerminalInput.MouseEvent me, float dt)
        {
            for (int i = 0; i < components.Count; i++)
            {
                BaseComponent component = components[i];
                component.HandleMouse(me, dt);
            }
        }

        public void HandleInput(ConsoleKeyInfo keyInfo)
        {
            for (int i = 0; i < components.Count; i++)
            {
                BaseComponent component = components[i];
                component.HandleInput(keyInfo);
            }
        }
    }
}
