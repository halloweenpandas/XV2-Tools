﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xv2CoreLib.EMD;
using Xv2CoreLib.Resource;
using YAXLib;

namespace Xv2CoreLib.EMG
{
    [YAXSerializeAs("EMG")]
    [Serializable]
    public class EMG_File
    {
        public const int EMG_SIGNATURE = 1196246307;
        public const string CLIPBOARD_ID = "XV2_EMG_FILE";

        [YAXAttributeForClass]
        public ushort I_04 { get; set; }

        [YAXCollection(YAXCollectionSerializationTypes.RecursiveWithNoContainingElement, EachElementName = "Mesh")]
        public List<EMG_Mesh> EmgMeshes { get; set; } = new List<EMG_Mesh>();

        #region LoadSave
        public static void CreateXml(string path)
        {
            var file = Read(File.ReadAllBytes(path), 0);

            YAXSerializer serializer = new YAXSerializer(typeof(EMG_File));
            serializer.SerializeToFile(file, path + ".xml");
        }

        public static void ConvertFromXml(string xmlPath)
        {
            string saveLocation = String.Format("{0}/{1}", Path.GetDirectoryName(xmlPath), Path.GetFileNameWithoutExtension(xmlPath));

            YAXSerializer serializer = new YAXSerializer(typeof(EMG_File), YAXSerializationOptions.DontSerializeNullObjects);
            var file = (EMG_File)serializer.DeserializeFromFile(xmlPath);

            File.WriteAllBytes(saveLocation, file.Write());
        }

        public static EMG_File Load(string path)
        {
            return Read(File.ReadAllBytes(path), 0);
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path, Write());
        }

        public static EMG_File Read(byte[] bytes, int offset)
        {
            EMG_File emgFile = new EMG_File();

            //Sanity check
            if (BitConverter.ToInt32(bytes, offset) != EMG_SIGNATURE)
                throw new InvalidDataException("EMG_File.Read: \"#EMG\" signature not found!");

            emgFile.I_04 = BitConverter.ToUInt16(bytes, offset + 4);

            ushort meshCount = BitConverter.ToUInt16(bytes, offset + 6);

            //Parse all meshes
            for (int i = 0; i < meshCount; i++)
            {
                int meshOffset = BitConverter.ToInt32(bytes, offset + 8 + (4 * i)) + offset;
                emgFile.EmgMeshes.Add(EMG_Mesh.Read(bytes, meshOffset));
            }

            return emgFile;
        }

        public byte[] Write(bool writeVertices = true, int absOffsetInFile = 0)
        {
            ushort meshCount = (ushort)(EmgMeshes != null ? EmgMeshes.Count : 0);
            List<byte> bytes = new List<byte>();

            //EMG Header:
            bytes.AddRange(BitConverter.GetBytes(EMG_SIGNATURE));
            bytes.AddRange(BitConverter.GetBytes(I_04));
            bytes.AddRange(BitConverter.GetBytes(meshCount));

            //Mesh offsets
            bytes.AddRange(new byte[4 * meshCount]);

            for (int i = 0; i < meshCount; i++)
            {
                //Pad file to be in alignment
                bytes.AddRange(new byte[Utils.CalculatePadding(bytes.Count, 16)]);

                //Add offset
                bytes = Utils.ReplaceRange(bytes, BitConverter.GetBytes(bytes.Count), 8 + (4 * i));

                bytes.AddRange(EmgMeshes[i].Write(writeVertices, absOffsetInFile + bytes.Count));
            }

            return bytes.ToArray();
        }

        #endregion

        /// <summary>
        /// Converts a <see cref="EMD_File"/> instance into a <see cref="EMG_File"/> instance, with additional support for merging EMB, DYTs and EMMs.
        /// </summary>
        /// <param name="emdFile">The EMD file to convert.</param>
        /// <param name="skeleton">The skeleton to go with the EMD file, if one exists. If the EMD file has any bone weights then an exception will be thrown if no skeleton is provided.</param>
        /// <param name="embIndex">The current merged EMB index. All texture references will be increased by this.</param>
        /// <param name="matNames">Dictionary containing the new material names. Submeshes will be renamed according to this (needed for when a merged EMM has multiple materials with the same name).</param>
        /// <param name="emdIdx">Sets the <see cref="EMG_File.I_04"/> value. Unknown function. </param>
        /// <returns></returns>
        public static EMG_File Convert(EMD_File emdFile, ESK.ESK_Skeleton skeleton, int embIndex, Dictionary<string, string> matNames, int emdIdx, bool hasDytSamler)
        {
            EMG_File emg = new EMG_File();
            emg.I_04 = (ushort)emdIdx;

            foreach (var model in emdFile.Models)
            {
                foreach (var mesh in model.Meshes)
                {
                    foreach (var submesh in mesh.Submeshes)
                    {
                        foreach (var triangleList in submesh.Triangles)
                        {
                            EMG_Mesh emgMesh = new EMG_Mesh();
                            emgMesh.AABB = mesh.AABB;
                            emgMesh.VertexFlags = submesh.VertexFlags;

                            //Texture samplers
                            EMG_TextureList textureList = new EMG_TextureList();
                            textureList.TextureSamplerDefs = submesh.TextureSamplerDefs;
                            emgMesh.TextureLists.Add(textureList);

                            foreach (var textureDef in textureList.TextureSamplerDefs)
                            {
                                if (hasDytSamler)
                                {
                                    textureDef.EmbIndex += (byte)(embIndex + 1);
                                }
                                else
                                {
                                    textureDef.EmbIndex += (byte)(embIndex);
                                }
                            }

                            if (hasDytSamler)
                            {
                                var textureSampler = textureList.TextureSamplerDefs[textureList.TextureSamplerDefs.Count - 1];
                                var dytSampler = new EMD_TextureSamplerDef()
                                {
                                    EmbIndex = (byte)embIndex,
                                    AddressModeU = EMD_TextureSamplerDef.AddressMode.Clamp,
                                    AddressModeV = EMD_TextureSamplerDef.AddressMode.Clamp,
                                    FilteringMag = EMD_TextureSamplerDef.Filtering.Linear,
                                    FilteringMin = EMD_TextureSamplerDef.Filtering.Linear,
                                    ScaleU = 1,
                                    ScaleV = 1
                                };

                                textureList.TextureSamplerDefs.Clear();
                                textureList.TextureSamplerDefs.Add(dytSampler); //for TOONvfx shaders, the dyt sampler is at index 0
                                textureList.TextureSamplerDefs.Add(textureSampler); //main texture is at index 1.

                            }

                            //Vertices
                            emgMesh.Vertices = submesh.Vertexes;

                            //Submesh
                            EMG_Submesh emgSubmesh = new EMG_Submesh();

                            string newMatName;

                            if (matNames.TryGetValue(submesh.Name, out newMatName))
                                emgSubmesh.MaterialName = newMatName;
                            else
                                emgSubmesh.MaterialName = submesh.Name;

                            //Faces
                            emgSubmesh.Faces = new short[triangleList.Faces.Count];

                            for (int i = 0; i < emgSubmesh.Faces.Length; i++)
                                emgSubmesh.Faces[i] = (short)triangleList.Faces[i];

                            //Bones
                            if(skeleton != null)
                            {
                                foreach (var bone in triangleList.Bones)
                                {

                                    ESK.ESK_Bone eskBone = skeleton.NonRecursiveBones.FirstOrDefault(x => x.Name == bone);

                                    if (eskBone != null)
                                        emgSubmesh.Bones.Add((ushort)eskBone.Index);
                                    else
                                        emgSubmesh.Bones.Add(0);
                                }
                            }
                            else if(emgMesh.VertexFlags.HasFlag(VertexFlags.BlendWeight))
                            {
                                emgMesh.VertexFlags = emgMesh.VertexFlags.RemoveFlag(VertexFlags.BlendWeight);
                            }

                            //Calc barycenter (LibXenoverse)
                            for (int i = 0; i < emgSubmesh.Faces.Length; i++)
                            {
                                if (emgSubmesh.Faces[i] >= emgMesh.Vertices.Count)
                                    continue;

                                EMD_Vertex vertex = emgMesh.Vertices[emgSubmesh.Faces[i]];

                                if (i == 0)
                                {
                                    emgSubmesh.BarycenterX = vertex.PositionX;
                                    emgSubmesh.BarycenterY = vertex.PositionY;
                                    emgSubmesh.BarycenterZ = vertex.PositionZ;
                                }
                                else
                                {

                                    emgSubmesh.BarycenterX *= (i / ((float)(i + 1))) + (vertex.PositionX / (i + 1));
                                    emgSubmesh.BarycenterY *= (i / ((float)(i + 1))) + (vertex.PositionY / (i + 1));
                                    emgSubmesh.BarycenterZ *= (i / ((float)(i + 1))) + (vertex.PositionZ / (i + 1));
                                }

                            }

                            emgSubmesh.BarycenterW = 1f;

                            emgMesh.Submeshes.Add(emgSubmesh);
                            emg.EmgMeshes.Add(emgMesh);
                        }

                    }

                }
            }

            return emg;
        }

        /// <summary>
        /// Convert a <see cref="EMD_File"/> instance into a <see cref="EMG_File"/> instance.
        /// </summary>
        public static EMG_File ConvertToEmg(EMD_File emdFile, ESK.ESK_Skeleton skeleton = null)
        {
            return Convert(emdFile, skeleton, 0, new Dictionary<string, string>(), 0, false);
        }

        /// <summary>
        /// Converts this <see cref="EMG_File"/> instance to a <see cref="EMD_File"/> instance. 
        /// DOES NOT SUPPORT SKINNING / ANIMATIONS.
        /// </summary>
        public EMD_File ConvertToEmd()
        {
            EMD_File emdFile = new EMD_File();
            emdFile.Version = 37507;

            //Root model entry. The entie EMG will be added onto this.
            EMD_Model emdModel = new EMD_Model();
            emdModel.Name = "EMG";
            emdFile.Models.Add(emdModel);

            foreach (EMG_Mesh mesh in EmgMeshes)
            {
                if (mesh.Submeshes.Count == 0) continue;

                foreach (EMG_Submesh submesh in mesh.Submeshes)
                {
                    EMD_Mesh emdMesh = new EMD_Mesh();
                    emdMesh.Name = submesh.MaterialName;
                    emdMesh.AABB = mesh.AABB;

                    EMD_Submesh emdSubmesh = new EMD_Submesh();
                    emdSubmesh.Name = submesh.MaterialName;
                    emdSubmesh.AABB = new EMD_AABB();

                    emdSubmesh.TextureSamplerDefs = (submesh.TextureListIndex >= mesh.TextureLists.Count) ? new AsyncObservableCollection<EMD_TextureSamplerDef>() : mesh.TextureLists[submesh.TextureListIndex].TextureSamplerDefs;
                    emdSubmesh.Vertexes = mesh.Vertices;
                    emdSubmesh.VertexFlags = mesh.VertexFlags;

                    //Blend weights are not currently supported by this method. Any weights will be removed upon conversion.
                    if (emdSubmesh.VertexFlags.HasFlag(VertexFlags.BlendWeight))
                    {
                        emdSubmesh.VertexFlags = emdSubmesh.VertexFlags.RemoveFlag(VertexFlags.BlendWeight);
                    }

                    EMD_Triangle triangleList = new EMD_Triangle();

                    foreach(short face in submesh.Faces)
                    {
                        triangleList.Faces.Add((ushort)face);
                    }

                    emdSubmesh.Triangles.Add(triangleList);
                    emdMesh.Submeshes.Add(emdSubmesh);
                    emdModel.Meshes.Add(emdMesh);
                }
            }

            return emdFile;
        }
    }

    [YAXSerializeAs("Mesh")]
    [Serializable]
    public class EMG_Mesh
    {
        [YAXAttributeForClass]
        public int I_08 { get; set; }
        [YAXAttributeForClass]
        public ushort Strips { get; set; }

        [YAXAttributeFor("VertexFlags")]
        [YAXSerializeAs("value")]
        public VertexFlags VertexFlags { get; set; }
        public EMD_AABB AABB { get; set; }

        public List<EMG_TextureList> TextureLists { get; set; } = new List<EMG_TextureList>();
        [YAXCollection(YAXCollectionSerializationTypes.RecursiveWithNoContainingElement, EachElementName = "Submesh")]
        public List<EMG_Submesh> Submeshes { get; set; } = new List<EMG_Submesh>();
        public List<EMD_Vertex> Vertices { get; set; } = new List<EMD_Vertex>();

        //Saving values:
        [YAXDontSerialize]
        public int VertexOffset { get; private set; }
        [YAXDontSerialize]
        public int StartOffset { get; private set; }

        public static EMG_Mesh Read(byte[] bytes, int offset)
        {
            EMG_Mesh mesh = new EMG_Mesh();
            mesh.VertexFlags = (VertexFlags)BitConverter.ToInt32(bytes, offset);
            mesh.I_08 = BitConverter.ToInt32(bytes, offset + 8);
            mesh.Strips = BitConverter.ToUInt16(bytes, offset + 24);
            mesh.AABB = EMD_AABB.Read(bytes, offset + 32);

            int textureListsCount = BitConverter.ToInt32(bytes, offset + 4);
            int textureListsOffset = BitConverter.ToInt32(bytes, offset + 12) + offset;
            ushort vertexCount = BitConverter.ToUInt16(bytes, offset + 16);
            ushort vertexSize = BitConverter.ToUInt16(bytes, offset + 18);
            int vertexOffset = BitConverter.ToInt32(bytes, offset + 20) + offset;
            ushort submeshCount = BitConverter.ToUInt16(bytes, offset + 26);
            int submeshListOffset = BitConverter.ToInt32(bytes, offset + 28) + offset;

            //Textures
            for (int i = 0; i < textureListsCount; i++)
            {
                int texOffset = BitConverter.ToInt32(bytes, textureListsOffset + (4 * i)) + offset;

                int count = BitConverter.ToInt32(bytes, texOffset);
                EMG_TextureList texList = new EMG_TextureList();
                texList.TextureSamplerDefs = EMD_TextureSamplerDef.Read(bytes, texOffset + 4, count);

                mesh.TextureLists.Add(texList);
            }

            //Submesh
            for (int i = 0; i < submeshCount; i++)
            {
                int submeshOffset = BitConverter.ToInt32(bytes, submeshListOffset + (4 * i)) + offset;

                mesh.Submeshes.Add(EMG_Submesh.Read(bytes, submeshOffset));
            }

            //Read vertices
            //These can either be in the EMG file, or after the skeleton in the parent EMO file. But when reading them, we dont need to care about that.
            mesh.Vertices = EMD_Vertex.ReadVertices(mesh.VertexFlags, bytes, vertexOffset, vertexCount, vertexSize);

            return mesh;
        }

        public List<byte> Write(bool writeVertices, int absOffsetInFile)
        {
            StartOffset = absOffsetInFile;

            List<byte> bytes = new List<byte>();

            int textureListsCount = (TextureLists != null) ? TextureLists.Count : 0;
            int submeshCount = (Submeshes != null) ? Submeshes.Count : 0;
            int vertexCount = (Vertices != null) ? Vertices.Count : 0;

            //Mesh header:
            bytes.AddRange(BitConverter.GetBytes((int)VertexFlags));
            bytes.AddRange(BitConverter.GetBytes(textureListsCount));
            bytes.AddRange(BitConverter.GetBytes(I_08));
            bytes.AddRange(BitConverter.GetBytes(textureListsCount > 0 ? 80 : 0)); //Textures offset (12). Since textures come first we can just set this now.
            bytes.AddRange(BitConverter.GetBytes((ushort)vertexCount));
            bytes.AddRange(BitConverter.GetBytes((ushort)EMD_Vertex.GetVertexSizeFromFlags(VertexFlags)));
            VertexOffset = bytes.Count + absOffsetInFile;
            bytes.AddRange(BitConverter.GetBytes(0)); //Vertex offset (20)
            bytes.AddRange(BitConverter.GetBytes(Strips));
            bytes.AddRange(BitConverter.GetBytes((ushort)submeshCount));
            bytes.AddRange(BitConverter.GetBytes(0)); //Submesh offset (28)
            bytes.AddRange(AABB.Write());

            //Textures:
            int pointerList = bytes.Count;
            bytes.AddRange(new byte[4 * textureListsCount]); //Write null bytes that will be replaced with the actual pointers 

            for (int i = 0; i < textureListsCount; i++)
            {
                //Update offset to point to this entry
                bytes = Utils.ReplaceRange(bytes, BitConverter.GetBytes(bytes.Count), pointerList + (4 * i));

                bytes.AddRange(TextureLists[i].Write());
            }

            //Submesh:
            if (submeshCount > 0)
            {
                pointerList = bytes.Count;
                bytes = Utils.ReplaceRange(bytes, BitConverter.GetBytes(pointerList), 28); //Add submesh offset to mesh header
                bytes.AddRange(new byte[4 * submeshCount]); //Write null bytes that will be replaced with the actual pointers 


                for (int i = 0; i < submeshCount; i++)
                {
                    //Pad file to 16-byte alignment
                    bytes.AddRange(new byte[Utils.CalculatePadding(bytes.Count, 16)]);

                    //Update offset to point to this entry
                    bytes = Utils.ReplaceRange(bytes, BitConverter.GetBytes(bytes.Count), pointerList + (4 * i));
                    bytes.AddRange(Submeshes[i].Write());
                }
            }

            if (writeVertices)
            {
                //Pad file to 16-byte alignment
                bytes.AddRange(new byte[Utils.CalculatePadding(bytes.Count, 16)]);

                //Vertices:
                bytes = Utils.ReplaceRange(bytes, BitConverter.GetBytes(bytes.Count), 20);
                bytes.AddRange(EMD_Vertex.GetBytes(Vertices, VertexFlags));
            }

            return bytes;
        }

    }

    [YAXSerializeAs("TextureList")]
    [Serializable]
    public class EMG_TextureList
    {
        [YAXCollection(YAXCollectionSerializationTypes.RecursiveWithNoContainingElement, EachElementName = "TextureSamplerDef")]
        public AsyncObservableCollection<EMD_TextureSamplerDef> TextureSamplerDefs { get; set; } = new AsyncObservableCollection<EMD_TextureSamplerDef>();

        public static EMG_TextureList Read(byte[] bytes, int offset)
        {
            EMG_TextureList textureList = new EMG_TextureList();
            textureList.TextureSamplerDefs = EMD_TextureSamplerDef.Read(bytes, offset + 4, BitConverter.ToInt32(bytes, offset + 0));

            return textureList;
        }

        public List<byte> Write()
        {
            List<byte> bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(TextureSamplerDefs.Count));
            bytes.AddRange(EMD_TextureSamplerDef.Write(TextureSamplerDefs));

            return bytes;
        }
    }

    [YAXSerializeAs("Submesh")]
    [Serializable]
    public class EMG_Submesh
    {
        [YAXAttributeForClass]
        [YAXSerializeAs("Material")]
        public string MaterialName { get; set; } = string.Empty;
        [YAXAttributeForClass]
        [YAXSerializeAs("TextureListIndex")]
        public ushort TextureListIndex { get; set; }

        [YAXAttributeFor("Barycenter")]
        [YAXSerializeAs("X")]
        public float BarycenterX { get; set; }
        [YAXAttributeFor("Barycenter")]
        [YAXSerializeAs("Y")]
        public float BarycenterY { get; set; }
        [YAXAttributeFor("Barycenter")]
        [YAXSerializeAs("Z")]
        public float BarycenterZ { get; set; }
        [YAXAttributeFor("Barycenter")]
        [YAXSerializeAs("W")]
        public float BarycenterW { get; set; } = 1f;

        public short[] Faces { get; set; }
        public List<ushort> Bones { get; set; } = new List<ushort>();

        public static EMG_Submesh Read(byte[] bytes, int offset)
        {
            EMG_Submesh submesh = new EMG_Submesh();
            submesh.BarycenterX = BitConverter.ToSingle(bytes, offset + 0);
            submesh.BarycenterY = BitConverter.ToSingle(bytes, offset + 4);
            submesh.BarycenterZ = BitConverter.ToSingle(bytes, offset + 8);
            submesh.BarycenterW = BitConverter.ToSingle(bytes, offset + 12);
            submesh.TextureListIndex = BitConverter.ToUInt16(bytes, offset + 16);

            ushort faceCount = BitConverter.ToUInt16(bytes, offset + 18);
            ushort boneCount = BitConverter.ToUInt16(bytes, offset + 20);

            submesh.MaterialName = StringEx.GetString(bytes, offset + 22, false, StringEx.EncodingType.UTF8, 32);

            //Faces
            submesh.Faces = new short[faceCount];

            for (int i = 0; i < faceCount; i++)
            {
                submesh.Faces[i] = BitConverter.ToInt16(bytes, offset + 54 + (i * 2));
            }

            //Bones
            int boneStartOffset = offset + 54 + (faceCount * 2);

            for (int i = 0; i < boneCount; i++)
            {
                submesh.Bones.Add(BitConverter.ToUInt16(bytes, boneStartOffset + (i * 2)));
            }

            return submesh;
        }

        public List<byte> Write()
        {
            List<byte> bytes = new List<byte>();

            bytes.AddRange(BitConverter.GetBytes(BarycenterX));
            bytes.AddRange(BitConverter.GetBytes(BarycenterY));
            bytes.AddRange(BitConverter.GetBytes(BarycenterZ));
            bytes.AddRange(BitConverter.GetBytes(BarycenterW));
            bytes.AddRange(BitConverter.GetBytes(TextureListIndex));
            bytes.AddRange(BitConverter.GetBytes((ushort)Faces.Length));
            bytes.AddRange(BitConverter.GetBytes((ushort)Bones.Count));

            if (MaterialName.Length > 32)
                throw new InvalidDataException("EMG_Submesh.MaterialName cannot be greater than 32 characters!");

            bytes.AddRange(Utils.GetStringBytes(MaterialName, 32));

            //Write Faces
            foreach (var face in Faces)
                bytes.AddRange(BitConverter.GetBytes(face));

            //Write Bones
            foreach (var bone in Bones)
                bytes.AddRange(BitConverter.GetBytes(bone));

            return bytes;
        }


        public int GetBoneIndex(int vertexIdx, int boneIdx)
        {
            if(boneIdx >= Bones.Count)
            {
                return Bones[0];
            }
            else
            {
                return Bones[boneIdx];
            }
            /*
            foreach (var vertex in Faces)
            {
                if (vertex == vertexIdx)
                {
                    return Bones[boneIdx];
                }
            }

            throw new InvalidDataException(String.Format("Could not get the bone idx for boneIndex: {0} on vertex: {1}", boneIdx, vertexIdx));
            */
        }
    }
}
