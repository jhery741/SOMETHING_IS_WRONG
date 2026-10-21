using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Cualquier objeto que el jugador puede señalar con [R] para reportar.</summary>
    public class Reportable : MonoBehaviour
    {
        public string displayName = "Objeto";
        public string roomId;
        public List<Anomaly> linked = new List<Anomaly>();
    }
}
