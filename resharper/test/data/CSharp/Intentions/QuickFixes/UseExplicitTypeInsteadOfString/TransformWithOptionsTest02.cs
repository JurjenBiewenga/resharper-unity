//${RUN:1}
using UnityEngine;

namespace A
{
    public class TestComponent : MonoBehaviour
    {
        
    }
}

namespace B
{
    public class TestComponent : MonoBehaviour
    {
        
    }
}

namespace C
{
    public class Test07 : MonoBehaviour
    {
        public void Method()
        {
            GetComponent("{caret}TestComponent");
        }
    }
}