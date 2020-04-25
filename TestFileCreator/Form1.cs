using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestFileCreator
{
    public partial class Form1 : Form
    {
        // Compteur de sauvegarde
        private int counter = 1;
        // Dossier de sauvegarde
        private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "generated_test_files");

        // Généateur de random 1
        private readonly Random rnd = new Random();

        // Cancel generation
        private bool cancelRequested = false;
        private bool isBusy = false;

        public Form1()
        {
            InitializeComponent();

            lblInfo.Text = "Idle";

            // Créer le dossier
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!isBusy)
            {
                // Début de la génération
                button1.Text = "Cancel";
                Generate();
                button1.Text = $"Generate {numericUpDown11.Value}";
            }
            else
            {
                AskCancellation();
            }
        }

        private void AskCancellation()
        {
            if (cancelRequested || !isBusy)
                return;

            if (MessageBox.Show("Confirm the cancellation of the generation\nCancel the generation ?", "Information", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                cancelRequested = true;
        }

        // Récupère le type (housing, production, transport) aléatoiremnt
        private int GetRndIndex(double val, IEnumerable<double> list)
        {
            for (int i = 0; i < list.Count(); i++)
            {
                if (val <= list.ElementAt(i))
                    return i;
                else
                    val -= list.ElementAt(i);
            }
            return -1;
        }

        // Generation de la position du noeud
        private PointD GenerateLocation(double max_x, double max_y, Random rnd1, Random rnd2)
        {
            PointD pt = new PointD()
            {
                x = rnd1.NextDouble() * 2 * max_x - max_x,
                y = rnd2.NextDouble() * 2 * max_y - max_y,
            };

            return pt;
        }

        // Generation du NBP
        private int GenerateNbp(int min, int max, Random rnd1, Random rnd2)
        {
            // 20% de chance d'avoir en dessus de la moitié de ce qui est choisi
            // Par ex. min = 1k, max = 5k
            // Donc on a 80% d'être entre 1k et 2.5k et 20% d'être entre 2.5k et 5k
            double percAbove = 0.2;
            double rndVal = rnd2.NextDouble();

            int nbpMin = min;
            int nbpMid = min + (max - min) / 2;
            int nbpMax = max;
            if (rndVal < percAbove)
                nbpMin = nbpMid;
            else
                nbpMax = nbpMid;

            return rnd.Next(nbpMin, nbpMax);
        }

        // Verification d'intersection entre un noeud et les autres noeuds
        private bool Intersect(Node nd, IEnumerable<Node> nodes)
        {
            foreach (Node item in nodes)
            {
                if (item.uid == nd.uid)
                    continue;

                // Si distance deux centres < somme des rayons, intersection
                double dx = nd.location.x - item.location.x;
                double dy = nd.location.y - item.location.y;

                double norme = Math.Sqrt(dx * dx + dy * dy);
                double sommeRayons = Math.Sqrt(nd.nbp) + Math.Sqrt(item.nbp);

                double dist_min = (double)numericUpDown10.Value;

                if (norme < sommeRayons + dist_min)
                    return true;
            }

            return false;
        }

        // Verification lien existant (ou invalide)
        private bool LinkExists(Link link, IEnumerable<Link> links)
        {
            if (link.uid1 == link.uid2)
                return true;

            foreach (var item in links)
            {
                bool c1 = link.uid1 == item.uid1;
                bool c2 = link.uid2 == item.uid2;

                if (c1 && c2)
                    return true;

                c1 = link.uid1 == item.uid2;
                c2 = link.uid2 == item.uid1;

                if (c1 && c2)
                    return true;
            }
            return false;
        }

        // Verification de connexion maximale pour Housing
        private bool LinkOverflow(Link link, IEnumerable<Node> nodes)
        {
            Node n1 = nodes.Where((x) => x.uid == link.uid1).First();

            if (n1.typeId == Node.nodeType.Housing && n1.linkCount >= 3)
                return true;

            Node n2 = nodes.Where((x) => x.uid == link.uid2).First();
            if (n2.typeId == Node.nodeType.Housing && n2.linkCount >= 3)
                return true;

            return false;
        }

        // Verification d'intersection entre un lien et les noeuds
        private bool LinkIntersect(Link link, IEnumerable<Node> nodes)
        {
            Node n1 = nodes.Where((x) => x.uid == link.uid1).First();
            Node n2 = nodes.Where((x) => x.uid == link.uid2).First();

            double ax = n1.location.x;
            double ay = n1.location.y;
            double bx = n2.location.x;
            double by = n2.location.y;

            double dist_min = (double)numericUpDown10.Value;

            // Intersection CL
            foreach (var item in nodes)
            {
                if (item.uid == n1.uid)
                    continue;
                if (item.uid == n2.uid)
                    continue;

                double cx = item.location.x;
                double cy = item.location.y;

                PointD P = new PointD();
                double t = ((cy - ay) * (by - ay) + (cx - ax) * (bx - ax)) / (Math.Pow((by - ay), 2) + Math.Pow((bx - ax), 2));
                P.x = ax + t * (bx - ax);
                P.y = ay + t * (by - ay);

                // En dehors du segment, faux de toute façon
                if (t < 0 || t > 1)
                    continue;

                if (norme(item.location, P) < (Math.Sqrt(item.nbp) + dist_min))
                    return true;
            }
            return false;
        }

        // Calcul de la norme
        double norme(PointD origine, PointD extremite)
        {
            return Math.Sqrt(Math.Pow(extremite.x - origine.x, 2) +
                    Math.Pow(extremite.y - origine.y, 2));
        }

        // Ajout d'un lien à la liste
        private void AddLink(Link lk, List<Link> links, IEnumerable<Node> nodes)
        {
            Node n1 = nodes.Where((x) => x.uid == lk.uid1).First();
            Node n2 = nodes.Where((x) => x.uid == lk.uid2).First();

            n1.linkCount++;
            n2.linkCount++;

            links.Add(lk);
        }

        // Generation des fichiers
        private void Generate()
        {
            Cursor = Cursors.WaitCursor;
            cancelRequested = false;
            isBusy = true;

            string filePath;

            // Paramètres

            int nbMaxNodes = (int)numericUpDown9.Value;

            double pcH = (double)numericUpDown1.Value;
            double pcP = (double)numericUpDown2.Value;
            double pcT = (double)numericUpDown3.Value;

            int minNbp = (int)numericUpDown7.Value;
            int maxNbp = (int)numericUpDown8.Value;

            double norm = pcH + pcP + pcT;
            pcH /= norm; pcP /= norm; pcT /= norm;

            double[] percentages = new double[] { pcH, pcP, pcT };

            double max_x = (double)numericUpDown5.Value;
            double max_y = (double)numericUpDown6.Value;

            int genCount = (int)numericUpDown11.Value;

            // Génération de 'gencount' fichiers
            int k;
            for (k = 0; k < genCount; k++)
            {
                Random rnd2 = new Random();

                lblInfo.Text = $"{k + 1}/{genCount}";
                Application.DoEvents();

                // Determiner le nom de fichier
                do
                {
                    filePath = Path.Combine(path, $"{counter++}.txt");
                } while (File.Exists(filePath));

                List<Node> nodeList = new List<Node>();

                int i;
                // Timeout de 10'000 essais lors de tentative de création de noeud sans intersection
                int timeoutCtrMax = 10000;
                int timeoutCtr;
                // Génération des noeuds
                for (i = 0; i < nbMaxNodes; i++)
                {
                    // Determiner le type de noeud
                    int index = GetRndIndex(rnd.NextDouble(), percentages);

                    // Créer le noeud
                    Node nd = new Node()
                    {
                        uid = i + 1,
                        typeId = (Node.nodeType)index,
                    };

                    // Tentative de création d'un noeud sans intersection
                    timeoutCtr = 0;
                    do
                    {
                        nd.location = GenerateLocation(max_x, max_y, rnd, rnd2);
                        nd.nbp = GenerateNbp(minNbp, maxNbp, rnd, rnd2);

                        timeoutCtr++;
                        if (timeoutCtr == timeoutCtrMax)
                            break;

                    } while (Intersect(nd, nodeList));

                    // Si timeout, ne pas l'ajouter
                    if (timeoutCtr == timeoutCtrMax)
                        break;

                    nodeList.Add(nd);
                }
                int nbNodes = i;

                // Si aucun noeud créée, interrompre
                if (nbNodes < 0)
                {
                    MessageBox.Show("Unable to create nodes with those paramters...\nPlease try again or contact the developper", "Erreur");
                    continue;
                }

                ;

                // Génération de liens
                List<Link> linkList = new List<Link>();

                // Timeout de 10'000 essais lors de tentative de création de lien sans intersection
                int nbMaxLinks = (int)numericUpDown4.Value;
                for (i = 0; i < nbMaxLinks; i++)
                {
                    Link lk = new Link();

                    // Tentative de création d'un lien sans intersection
                    timeoutCtr = 0;
                    do
                    {
                        lk.uid1 = rnd.Next(1, nbNodes);
                        lk.uid2 = rnd2.Next(1, nbNodes);
                        lk.Validate();

                        timeoutCtr++;
                        if (timeoutCtr == timeoutCtrMax)
                            break;

                    } while (LinkExists(lk, linkList) || LinkIntersect(lk, nodeList) || LinkOverflow(lk, nodeList));

                    if (timeoutCtr == timeoutCtrMax)
                        break;

                    AddLink(lk, linkList, nodeList);
                }

                ;

                // Sauvegarde du fichier
                string text = GenerateText(nodeList, linkList);
                File.WriteAllText(filePath, text);
                ;

                // Cancellation
                if (cancelRequested)
                {
                    lblInfo.Text = "Cancelled";
                    break;
                }

            }

            if (!cancelRequested)
            {
                lblInfo.Text = "Idle";
                k--;
            }

            isBusy = false;
            Cursor = Cursors.Default;

            MessageBox.Show($"Generation complete !\n{k + 1} files created at :\n'{path}'", "Information");
        }

        private string GenerateText(IEnumerable<Node> nodes, IEnumerable<Link> links)
        {
            var selectedNodes = nodes.Where((x) => x.typeId == Node.nodeType.Housing);

            StringBuilder output = new StringBuilder();

            // Paramètre de génération
            output.AppendLine($"# Automatically generated test file. Parameters are : ");
            output.AppendLine($"# Housing proportion : {numericUpDown1.Value}");
            output.AppendLine($"# Production proportion : {numericUpDown2.Value}");
            output.AppendLine($"# Transport proportion : {numericUpDown3.Value}");
            output.AppendLine($"# Max nodes : {numericUpDown9.Value}");
            output.AppendLine($"# Max links : {numericUpDown4.Value}");
            output.AppendLine($"# Max coord. x : {numericUpDown5.Value}");
            output.AppendLine($"# Max coord. y : {numericUpDown6.Value}");
            output.AppendLine($"# Min NBP : {numericUpDown7.Value}");
            output.AppendLine($"# Max NBP : {numericUpDown8.Value}");
            output.AppendLine($"# Dist_min : {numericUpDown10.Value}");
            output.AppendLine($"# Generated nodes : {nodes.Count()}");
            output.AppendLine($"# Generated links : {links.Count()}");
            output.AppendLine($"# Developed by Esseiva N.");
            output.AppendLine("");

            output.AppendLine("");
            output.AppendLine($"# Housing");
            output.AppendLine(selectedNodes.Count().ToString());
            foreach (var item in selectedNodes)
            {
                output.AppendLine(item.ToString());
            }

            selectedNodes = nodes.Where((x) => x.typeId == Node.nodeType.Production);

            output.AppendLine("");
            output.AppendLine($"# Production");
            output.AppendLine(selectedNodes.Count().ToString());
            foreach (var item in selectedNodes)
            {
                output.AppendLine(item.ToString());
            }

            selectedNodes = nodes.Where((x) => x.typeId == Node.nodeType.Transport);

            output.AppendLine("");
            output.AppendLine($"# Transport");
            output.AppendLine(selectedNodes.Count().ToString());
            foreach (var item in selectedNodes)
            {
                output.AppendLine(item.ToString());
            }

            output.AppendLine("");
            output.AppendLine($"# Links");
            output.AppendLine(links.Count().ToString());
            foreach (var item in links)
            {
                output.AppendLine(item.ToString());
            }

            return output.ToString();
        }

        private class Node
        {
            public int uid;
            public PointD location;
            public int nbp;

            // Type de noeud
            public nodeType typeId;

            // Nombre de lien lié à ce noeud
            public int linkCount = 0;

            public enum nodeType
            {
                Housing,
                Production,
                Transport,
            }

            public override string ToString()
            {
                return $"\t{uid}\t{location.x.ToString("#0.000000", CultureInfo.InvariantCulture)}\t{location.y.ToString("#0.000000", CultureInfo.InvariantCulture)}\t{nbp}";
            }
        }

        private class Link
        {
            public int uid1;
            public int uid2;

            public void Validate()
            {
                if (uid1 > uid2)
                {
                    int temp = uid1;
                    uid1 = uid2;
                    uid2 = temp;
                }
            }

            public override string ToString()
            {
                return $"\t{uid1}\t{uid2}";
            }
        }

        private class PointD
        {
            public double x;
            public double y;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Process.Start(path);
        }

        private void numericUpDown11_ValueChanged(object sender, EventArgs e)
        {
            button1.Text = $"Generate ({numericUpDown11.Value})";
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                AskCancellation();
            }
        }
    }
}
