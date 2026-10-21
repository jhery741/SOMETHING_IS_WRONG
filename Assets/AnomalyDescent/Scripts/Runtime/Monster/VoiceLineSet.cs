using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Momento en que puede sonar una línea de voz.</summary>
    public enum VoiceContext
    {
        Ambiente, Saludo, Foto, ReporteBien, ReporteMal, Puerta, Correr, Esconderse, Refugio, Quieto,
        CierreVuelta, Habitacion, Pregunta, RespuestaVerdad, RespuestaMentira, RespuestaSilencio, Juego,
        Aparicion, Persecucion, Perdida, Retirada, Final
    }

    /// <summary>Cómo se comprueba si una respuesta es verdad (no hay IA: reglas fijas sobre el estado del juego).</summary>
    public enum QuestionCheck { Ninguna, DondeEstas, HasVistoAlgo, Jugar }

    public enum OptionKind { Verdad, Evasiva, Silencio, Si, No }

    [System.Serializable]
    public class VoiceOption
    {
        [Tooltip("Texto corto que elige el jugador. {habitacion} = nombre de la habitación donde está.")]
        public string label;
        public OptionKind kind;
        [Tooltip("Cambio de atención si la respuesta es verdad (o si no se puede comprobar).")]
        public float attention;
        [Tooltip("Cambio de atención si se comprueba que es mentira.")]
        public float attentionIfLie = 1f;
    }

    [System.Serializable]
    public class VoiceLine
    {
        [Tooltip("Identificador = nombre del archivo de audio en Resources/Voz/<id> si 'clip' está vacío.")]
        public string id;
        public VoiceContext context;
        [TextArea] public string text;
        [Tooltip("Grabación. Vacío = se busca Resources/Voz/<id>; si tampoco existe suena un susurro provisional + subtítulo.")]
        public AudioClip clip;
        [Range(0, 4)] public int minStage;
        [Range(0, 4)] public int maxStage = 4;
        [Tooltip("Solo para líneas de habitación (Habitacion).")]
        public string roomId;
        public QuestionCheck check;
        public List<VoiceOption> options = new List<VoiceOption>();
    }

    /// <summary>
    /// Todas las líneas de voz pregrabadas de El Ciego. Son DATOS editables (crea el asset con
    /// "ANOMALY DESCENT/Etapas/Crear asset de voces" y edítalo en el Inspector); no hay diálogo generativo:
    /// las líneas solo se eligen por contexto, etapa de la presencia y enfriamiento.
    /// </summary>
    [CreateAssetMenu(menuName = "ANOMALY DESCENT/Voces del monstruo", fileName = "Voces_ElCiego")]
    public class VoiceLineSet : ScriptableObject
    {
        public string speakerName = "El Ciego";
        public List<VoiceLine> lines = new List<VoiceLine>();

        public static VoiceLineSet CreateDefault()
        {
            var s = CreateInstance<VoiceLineSet>();
            s.name = "Voces_ElCiego (por defecto)";
            s.Fill();
            return s;
        }

        static VoiceLine L(string id, VoiceContext c, string text, int min = 1, int max = 4)
        {
            return new VoiceLine { id = id, context = c, text = text, minStage = min, maxStage = max };
        }

        static VoiceOption O(string label, OptionKind k, float att, float lie = 1f)
        {
            return new VoiceOption { label = label, kind = k, attention = att, attentionIfLie = lie };
        }

        public void Fill()
        {
            lines.Clear();
            lines.Add(L("saludo_01", VoiceContext.Saludo, "¿Hola? ¿Hay alguien en casa?", 0, 1));
            lines.Add(L("amb_01", VoiceContext.Ambiente, "Esta casa era más grande antes.", 0, 2));
            lines.Add(L("amb_02", VoiceContext.Ambiente, "Shhh. Escucha.", 0, 3));
            lines.Add(L("amb_03", VoiceContext.Ambiente, "Me gusta cuando vienes.", 1, 3));
            lines.Add(L("amb_04", VoiceContext.Ambiente, "Las cosas no se mueven solas. Casi nunca.", 1, 3));
            lines.Add(L("amb_05", VoiceContext.Ambiente, "Cuento las puertas. Siempre me sale una de más.", 1, 3));
            lines.Add(L("amb_alerta_01", VoiceContext.Ambiente, "Te estoy oyendo respirar.", 2, 4));
            lines.Add(L("amb_alerta_02", VoiceContext.Ambiente, "Cada vez estás más cerca. O yo.", 2, 4));
            lines.Add(L("amb_alerta_03", VoiceContext.Ambiente, "No me hagas ir a buscarte.", 2, 3));
            lines.Add(L("foto_01", VoiceContext.Foto, "Clic. ¿He salido bien?"));
            lines.Add(L("foto_02", VoiceContext.Foto, "Esa luz me molesta."));
            lines.Add(L("rep_ok_01", VoiceContext.ReporteBien, "Eso no estaba así, ¿verdad?"));
            lines.Add(L("rep_ok_02", VoiceContext.ReporteBien, "Bien visto. Lo devolveré a su sitio... por ahora."));
            lines.Add(L("rep_mal_01", VoiceContext.ReporteMal, "Te has equivocado. Lo he oído."));
            lines.Add(L("rep_mal_02", VoiceContext.ReporteMal, "Eso siempre estuvo ahí."));
            lines.Add(L("puerta_01", VoiceContext.Puerta, "¿Quién ha abierto esa puerta?"));
            lines.Add(L("puerta_02", VoiceContext.Puerta, "No des portazos en mi casa."));
            lines.Add(L("correr_01", VoiceContext.Correr, "No corras por la casa."));
            lines.Add(L("correr_02", VoiceContext.Correr, "Corres como los otros."));
            lines.Add(L("esconder_01", VoiceContext.Esconderse, "¿Al escondite? Yo también sabía jugar."));
            lines.Add(L("refugio_01", VoiceContext.Refugio, "Ahí no puedo entrar. Pero puedo esperar.", 2, 4));
            lines.Add(L("quieto_01", VoiceContext.Quieto, "¿Sigues ahí?"));
            lines.Add(L("vuelta_01", VoiceContext.CierreVuelta, "Otra vuelta. Me gusta este juego.", 0, 4));
            lines.Add(L("vuelta_02", VoiceContext.CierreVuelta, "Lo he dejado todo como estaba. Casi.", 0, 4));
            foreach (var r in new[] { new[] { "dormitorio", "el dormitorio" }, new[] { "pasillo", "el pasillo" }, new[] { "sala", "la sala" },
                                      new[] { "cocina", "la cocina" }, new[] { "bano", "el baño" }, new[] { "recibidor", "el recibidor" } })
            {
                var l = L("hab_" + r[0], VoiceContext.Habitacion, "Estás en " + r[1] + ".", 2, 4);
                l.roomId = r[0];
                lines.Add(l);
            }
            var q1 = L("preg_donde", VoiceContext.Pregunta, "¿Dónde estás?");
            q1.check = QuestionCheck.DondeEstas;
            q1.options.Add(O("«En {habitacion}.»", OptionKind.Verdad, -0.5f));
            q1.options.Add(O("«Lejos de ti.»", OptionKind.Evasiva, 0.5f));
            q1.options.Add(O("(callar)", OptionKind.Silencio, 0.25f));
            lines.Add(q1);
            var q2 = L("preg_visto", VoiceContext.Pregunta, "¿Has visto algo raro?");
            q2.check = QuestionCheck.HasVistoAlgo;
            q2.options.Add(O("«Sí.»", OptionKind.Si, -0.5f, 1f));
            q2.options.Add(O("«No.»", OptionKind.No, -0.5f, 1f));
            q2.options.Add(O("(callar)", OptionKind.Silencio, 0.25f));
            lines.Add(q2);
            var q3 = L("preg_jugar", VoiceContext.Pregunta, "¿Jugamos a algo?", 1, 3);
            q3.check = QuestionCheck.Jugar;
            q3.options.Add(O("«Vale.»", OptionKind.Si, -0.5f));
            q3.options.Add(O("«No.»", OptionKind.No, 0.5f));
            q3.options.Add(O("(callar)", OptionKind.Silencio, 0.25f));
            lines.Add(q3);
            var q4 = L("preg_miedo", VoiceContext.Pregunta, "¿Tienes miedo?");
            q4.options.Add(O("«Sí.»", OptionKind.Si, -0.5f));
            q4.options.Add(O("«No.»", OptionKind.No, 0.5f));
            q4.options.Add(O("(callar)", OptionKind.Silencio, 0.25f));
            lines.Add(q4);
            lines.Add(L("resp_verdad", VoiceContext.RespuestaVerdad, "Lo sé.", 0, 4));
            lines.Add(L("resp_mentira", VoiceContext.RespuestaMentira, "Mientes.", 0, 4));
            lines.Add(L("resp_silencio", VoiceContext.RespuestaSilencio, "¿No me contestas?", 0, 4));
            lines.Add(L("juego_01", VoiceContext.Juego, "Tres golpes. Encuéntrame.", 0, 4));
            lines.Add(L("apar_01", VoiceContext.Aparicion, "Te oigo. Te oigo muy cerca.", 3, 4));
            lines.Add(L("apar_02", VoiceContext.Aparicion, "No te muevas.", 3, 4));
            lines.Add(L("pers_01", VoiceContext.Persecucion, "¡Ahí estás!", 0, 4));
            lines.Add(L("pers_02", VoiceContext.Persecucion, "¡No corras!", 0, 4));
            lines.Add(L("perd_01", VoiceContext.Perdida, "¿Dónde te has metido?", 0, 4));
            lines.Add(L("perd_02", VoiceContext.Perdida, "Volverás a hacer ruido.", 0, 4));
            lines.Add(L("ret_01", VoiceContext.Retirada, "Luego. Luego jugamos.", 0, 4));
            lines.Add(L("fin_01", VoiceContext.Final, "¿Te vas? Nadie se va.", 0, 4));
        }
    }
}
