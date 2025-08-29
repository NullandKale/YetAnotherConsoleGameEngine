using ConsoleGame.Entities;
using ConsoleGame.Renderer;

namespace ConsoleGame.Components
{
    public class BaseComponent
    {
        public BaseEntity Parent;

        public virtual void Update(double deltaTime)
        {
            // Base does nothing
        }

        public virtual void HandleMouse(TerminalInput.MouseEvent me, float dt)
        {
            // Base does nothing
        }

        public virtual void HandleInput(ConsoleKeyInfo keyInfo)
        {
            // Base does nothing
        }
    }
}
