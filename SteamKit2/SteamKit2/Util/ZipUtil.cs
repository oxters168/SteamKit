/*
 * This file is subject to the terms and conditions defined in
 * file 'license.txt', which is part of this source code package.
 */



using System;
using System.IO;
//using Ionic.Zlib;
//using System.IO.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System.Text;

namespace SteamKit2
{
    static class ZipUtil
    {
        private static UInt32 LocalFileHeader = 0x04034b50;
        private static UInt32 CentralDirectoryHeader = 0x02014b50;
        private static UInt32 EndOfDirectoryHeader = 0x06054b50;

        private static UInt16 DeflateCompression = 8;
        private static UInt16 StoreCompression = 0;

        private static UInt16 Version = 20;

        public static byte[] Decompress( byte[] buffer )
        {
            using ( MemoryStream ms = new MemoryStream( buffer ) )
            using ( BinaryReader reader = new BinaryReader( ms ) )
            {
                if ( !PeekHeader( reader, LocalFileHeader ) )
                {
                    throw new Exception( "Expecting LocalFileHeader at start of stream" );
                }

                string fileName;
                UInt32 decompressedSize;
                UInt16 compressionMethod;
                byte[] compressedBuffer = ReadLocalFile( reader, out fileName, out decompressedSize, out compressionMethod );

                if ( !PeekHeader( reader, CentralDirectoryHeader ) )
                {
                    throw new Exception( "Expecting CentralDirectoryHeader following filename" );
                }

                string cdrFileName;
                /*Int32 relativeOffset =*/
                ReadCentralDirectory( reader, out cdrFileName );

                if ( !PeekHeader( reader, EndOfDirectoryHeader ) )
                {
                    throw new Exception( "Expecting EndOfDirectoryHeader following CentralDirectoryHeader" );
                }

                /*UInt32 count =*/
                ReadEndOfDirectory( reader );

                if ( compressionMethod == DeflateCompression )
                    return InflateBuffer( compressedBuffer, decompressedSize );
                else
                    return compressedBuffer;
            }
        }
        public static Stream Decompress( Stream reader )
        {
            if ( !PeekHeader( reader, LocalFileHeader ) )
            {
                throw new Exception( "Expecting LocalFileHeader at start of stream" );
            }

            string fileName;
            UInt32 decompressedSize;
            UInt16 compressionMethod;
            Stream compressedStream = ReadLocalFile( reader, out fileName, out decompressedSize, out compressionMethod );
            //byte[] compressedBuffer = ReadLocalFile( reader, out fileName, out decompressedSize, out compressionMethod );

            if ( !PeekHeader( reader, CentralDirectoryHeader ) )
            {
                throw new Exception( "Expecting CentralDirectoryHeader following filename" );
            }

            string cdrFileName;
            /*Int32 relativeOffset =*/
            ReadCentralDirectory( reader, out cdrFileName );

            if ( !PeekHeader( reader, EndOfDirectoryHeader ) )
            {
                throw new Exception( "Expecting EndOfDirectoryHeader following CentralDirectoryHeader" );
            }

            /*UInt32 count =*/
            ReadEndOfDirectory( reader );

            if ( compressionMethod == DeflateCompression )
            {
                using ( compressedStream )
                {
                    return InflateBuffer( compressedStream, decompressedSize );
                }
            }
            else
                return compressedStream;
        }

        public static byte[] Compress( byte[] buffer )
        {
            using ( MemoryStream ms = new MemoryStream() )
            using ( BinaryWriter writer = new BinaryWriter( ms ) )
            {
                uint checkSum = Crc32.Compute( buffer );

                byte[] compressed = DeflateBuffer( buffer );

                Int32 poslocal = WriteHeader( writer, LocalFileHeader );
                WriteLocalFile( writer, "z", checkSum, ( UInt32 )buffer.Length, compressed );

                Int32 posCDR = WriteHeader( writer, CentralDirectoryHeader );
                UInt32 CDRSize = WriteCentralDirectory( writer, "z", checkSum, ( UInt32 )compressed.Length, ( UInt32 )buffer.Length, poslocal );

                /*Int32 posEOD =*/
                WriteHeader( writer, EndOfDirectoryHeader );
                WriteEndOfDirectory( writer, 1, CDRSize, posCDR );

                return ms.ToArray();
            }
        }


        private static Int32 WriteHeader( BinaryWriter writer, UInt32 header )
        {
            Int32 position = ( Int32 )writer.BaseStream.Position;

            writer.Write( header );

            return position;
        }

        private static void WriteEndOfDirectory( BinaryWriter writer, UInt32 count, UInt32 CDRSize, Int32 CDROffset )
        {
            writer.Write( ( UInt16 )0 ); // diskNumber
            writer.Write( ( UInt16 )0 ); // CDRDisk
            writer.Write( ( UInt16 )count ); // CDRCount
            writer.Write( ( UInt16 )1 ); // CDRTotal

            writer.Write( ( UInt32 )CDRSize ); // CDRSize
            writer.Write( ( Int32 )CDROffset ); // CDROffset

            writer.Write( ( UInt16 )0 ); // commentLength
        }

        private static UInt32 WriteCentralDirectory( BinaryWriter writer, string fileName, UInt32 CRC, UInt32 compressedSize, UInt32 decompressedSize, Int32 localHeaderOffset )
        {
            UInt32 pos = ( UInt32 )writer.BaseStream.Position;

            writer.Write( Version ); // versionGenerator
            writer.Write( Version ); // versionExtract
            writer.Write( ( UInt16 )0 ); // bitflags
            writer.Write( DeflateCompression ); // compression

            writer.Write( ( UInt16 )0 ); // modTime
            writer.Write( ( UInt16 )0 ); // createTime
            writer.Write( CRC ); // CRC

            writer.Write( compressedSize ); // compressedSize
            writer.Write( decompressedSize ); // decompressedSize

            writer.Write( ( UInt16 )Encoding.UTF8.GetByteCount( fileName ) ); // nameLength
            writer.Write( ( UInt16 )0 ); // fieldLength
            writer.Write( ( UInt16 )0 ); // commentLength

            writer.Write( ( UInt16 )0 ); // diskNumber
            writer.Write( ( UInt16 )1 ); // internalAttributes
            writer.Write( ( UInt32 )32 ); // externalAttributes

            writer.Write( localHeaderOffset ); // relativeOffset

            writer.Write( Encoding.UTF8.GetBytes( fileName ) ); // filename

            return ( ( UInt32 )writer.BaseStream.Position - pos ) + 4;
        }

        private static void WriteLocalFile( BinaryWriter writer, string fileName, UInt32 CRC, UInt32 decompressedSize, byte[] processedBuffer )
        {
            writer.Write( Version ); // version
            writer.Write( ( UInt16 )0 ); // bitflags
            writer.Write( DeflateCompression ); // compression

            writer.Write( ( UInt16 )0 ); // modTime
            writer.Write( ( UInt16 )0 ); // createTime
            writer.Write( CRC ); // CRC

            writer.Write( processedBuffer.Length ); // compressedSize
            writer.Write( decompressedSize ); // decompressedSize

            writer.Write( ( UInt16 )Encoding.UTF8.GetByteCount( fileName ) ); // nameLength
            writer.Write( ( UInt16 )0 ); // fieldLength

            writer.Write( Encoding.UTF8.GetBytes( fileName ) ); // filename
            writer.Write( processedBuffer ); // contents
        }


        private static bool PeekHeader( BinaryReader reader, UInt32 expecting )
        {
            UInt32 header = reader.ReadUInt32();

            return header == expecting;
        }
        private static bool PeekHeader( Stream stream, UInt32 expecting )
        {
            UInt32 header = stream.ReadUInt32();

            return header == expecting;
        }

        private static UInt32 ReadEndOfDirectory( BinaryReader reader )
        {
            /*UInt16 diskNumber =*/
            reader.ReadUInt16();
            /*UInt16 CDRDisk =*/
            reader.ReadUInt16();
            UInt16 CDRCount = reader.ReadUInt16();
            /*UInt16 CDRTotal =*/
            reader.ReadUInt16();

            /*UInt32 CDRSize =*/
            reader.ReadUInt32();
            /*Int32 CDROffset =*/
            reader.ReadInt32();

            UInt16 commentLength = reader.ReadUInt16();
            /*byte[] comment =*/
            reader.ReadBytes( commentLength );

            return CDRCount;
        }
        private static UInt32 ReadEndOfDirectory( Stream stream )
        {
            /*UInt16 diskNumber =*/
            stream.ReadUInt16();
            /*UInt16 CDRDisk =*/
            stream.ReadUInt16();
            UInt16 CDRCount = stream.ReadUInt16();
            /*UInt16 CDRTotal =*/
            stream.ReadUInt16();

            /*UInt32 CDRSize =*/
            stream.ReadUInt32();
            /*Int32 CDROffset =*/
            stream.ReadUInt32();

            UInt16 commentLength = stream.ReadUInt16();
            /*byte[] comment =*/
            stream.Seek( commentLength, SeekOrigin.Current );

            return CDRCount;
        }

        private static Int32 ReadCentralDirectory( BinaryReader reader, out String fileName )
        {
            /*UInt16 versionGenerator =*/
            reader.ReadUInt16();
            /*UInt16 versionExtract =*/
            reader.ReadUInt16();
            /*UInt16 bitflags =*/
            reader.ReadUInt16();
            UInt16 compression = reader.ReadUInt16();

            if ( compression != DeflateCompression && compression != StoreCompression )
            {
                throw new Exception( "Invalid compression method " + compression );
            }

            /*UInt16 modtime =*/
            reader.ReadUInt16();
            /*UInt16 createtime =*/
            reader.ReadUInt16();
            /*UInt32 crc =*/
            reader.ReadUInt32();

            /*UInt32 compressedSize =*/
            reader.ReadUInt32();
            /*UInt32 decompressedSize =*/
            reader.ReadUInt32();

            UInt16 nameLength = reader.ReadUInt16();
            UInt16 fieldLength = reader.ReadUInt16();
            UInt16 commentLength = reader.ReadUInt16();

            /*UInt16 diskNumber =*/
            reader.ReadUInt16();
            /*UInt16 internalAttributes =*/
            reader.ReadUInt16();
            /*UInt32 externalAttributes =*/
            reader.ReadUInt32();

            Int32 relativeOffset = reader.ReadInt32();

            byte[] name = reader.ReadBytes( nameLength );
            /*byte[] fields =*/
            reader.ReadBytes( fieldLength );
            /*byte[] comment =*/
            reader.ReadBytes( commentLength );

            fileName = Encoding.UTF8.GetString( name );
            return relativeOffset;
        }
        private static Int32 ReadCentralDirectory( Stream stream, out String fileName )
        {
            /*UInt16 versionGenerator =*/
            stream.ReadUInt16();
            /*UInt16 versionExtract =*/
            stream.ReadUInt16();
            /*UInt16 bitflags =*/
            stream.ReadUInt16();
            UInt16 compression = stream.ReadUInt16();

            if ( compression != DeflateCompression && compression != StoreCompression )
            {
                throw new Exception( "Invalid compression method " + compression );
            }

            /*UInt16 modtime =*/
            stream.ReadUInt16();
            /*UInt16 createtime =*/
            stream.ReadUInt16();
            /*UInt32 crc =*/
            stream.ReadUInt32();

            /*UInt32 compressedSize =*/
            stream.ReadUInt32();
            /*UInt32 decompressedSize =*/
            stream.ReadUInt32();

            UInt16 nameLength = stream.ReadUInt16();
            UInt16 fieldLength = stream.ReadUInt16();
            UInt16 commentLength = stream.ReadUInt16();

            /*UInt16 diskNumber =*/
            stream.ReadUInt16();
            /*UInt16 internalAttributes =*/
            stream.ReadUInt16();
            /*UInt32 externalAttributes =*/
            stream.ReadUInt32();

            Int32 relativeOffset = stream.ReadInt32();

            byte[] name = new byte[ nameLength ];
            stream.Read( name, 0, nameLength );
            /*byte[] fields =*/
            stream.Seek( fieldLength, SeekOrigin.Current );
            /*byte[] comment =*/
            stream.Seek( commentLength, SeekOrigin.Current );

            fileName = Encoding.UTF8.GetString( name );
            return relativeOffset;
        }

        private static byte[] ReadLocalFile( BinaryReader reader, out String fileName, out UInt32 decompressedSize, out UInt16 compressionMethod )
        {
            /*UInt16 version =*/
            reader.ReadUInt16();
            /*UInt16 bitflags =*/
            reader.ReadUInt16();
            compressionMethod = reader.ReadUInt16();

            if ( compressionMethod != DeflateCompression && compressionMethod != StoreCompression )
            {
                throw new Exception( "Invalid compression method " + compressionMethod );
            }

            /*UInt16 modtime =*/
            reader.ReadUInt16();
            /*UInt16 createtime =*/
            reader.ReadUInt16();
            /*UInt32 crc =*/
            reader.ReadUInt32();

            UInt32 compressedSize = reader.ReadUInt32();
            decompressedSize = reader.ReadUInt32();

            UInt16 nameLength = reader.ReadUInt16();
            UInt16 fieldLength = reader.ReadUInt16();

            byte[] name = reader.ReadBytes( nameLength );
            /*byte[] fields =*/
            reader.ReadBytes( fieldLength );

            fileName = Encoding.UTF8.GetString( name );

            return reader.ReadBytes( ( int )compressedSize );
        }
        private static Stream ReadLocalFile( Stream reader, out String fileName, out UInt32 decompressedSize, out UInt16 compressionMethod )
        {
            /*UInt16 version =*/
            reader.ReadUInt16();
            /*UInt16 bitflags =*/
            reader.ReadUInt16();
            compressionMethod = reader.ReadUInt16();

            if ( compressionMethod != DeflateCompression && compressionMethod != StoreCompression )
            {
                throw new Exception( "Invalid compression method " + compressionMethod );
            }

            /*UInt16 modtime =*/
            reader.ReadUInt16();
            /*UInt16 createtime =*/
            reader.ReadUInt16();
            /*UInt32 crc =*/
            reader.ReadUInt32();

            UInt32 compressedSize = reader.ReadUInt32();
            decompressedSize = reader.ReadUInt32();

            UInt16 nameLength = reader.ReadUInt16();
            UInt16 fieldLength = reader.ReadUInt16();

            byte[] name = new byte[ nameLength ];
            reader.Read( name, 0, nameLength );
            /*byte[] fields =*/
            reader.Seek( fieldLength, SeekOrigin.Current );

            fileName = Encoding.UTF8.GetString( name );

            MemoryStream ms = new MemoryStream( ( int )compressedSize );
            CopyTo( reader, ms, compressedSize );
            ms.Position = 0;
            return ms;
        }
        public static void CopyTo( Stream from, Stream to, long amount, int bufferSize = 81920 )
        {
            long totalCopied = 0;
            byte[] buffer = new byte[ bufferSize ];
            int actualAmountRead;
            do
            {
                int readLength = ( int )Math.Min( amount - totalCopied, bufferSize );
                actualAmountRead = from.Read( buffer, 0, readLength );
                if ( actualAmountRead > 0 )
                    to.Write( buffer, 0, actualAmountRead );
                totalCopied += actualAmountRead;
            }
            while ( actualAmountRead > 0 );
        }

        private static byte[] InflateBuffer( byte[] compressedBuffer, UInt32 decompressedSize )
        {
            using ( MemoryStream ms = new MemoryStream( compressedBuffer ) )
            //using ( DeflateStream deflateStream = new DeflateStream( ms, CompressionMode.Decompress ) )
            //var inflater = new Inflater( true );
            using ( InflaterInputStream deflateStream = new InflaterInputStream( ms, new Inflater( true ) ) )
            {
                byte[] inflated = new byte[ decompressedSize ];
                deflateStream.Read( inflated, 0, inflated.Length );

                return inflated;
            }
        }
        private static Stream InflateBuffer( Stream ms, UInt32 decompressedSize )
        {
            MemoryStream stream = new MemoryStream( ( int )decompressedSize );
            //using ( MemoryStream ms = new MemoryStream( compressedBuffer ) )
            //using ( DeflateStream deflateStream = new DeflateStream( ms, CompressionMode.Decompress ) )
            using ( InflaterInputStream deflateStream = new InflaterInputStream( ms, new Inflater( true ) ) )
            {
                //byte[] inflated = new byte[ decompressedSize ];
                //deflateStream.Read( inflated, 0, inflated.Length );

                CopyTo( deflateStream, stream, decompressedSize );
            }
            stream.Position = 0;
            return stream;
        }

        private static byte[] DeflateBuffer( byte[] uncompressedBuffer )
        {
            using ( MemoryStream ms = new MemoryStream() )
            {
                //using ( DeflateStream deflateStream = new DeflateStream( ms, CompressionMode.Compress ) )
                using ( DeflaterOutputStream deflateStream = new DeflaterOutputStream( ms, new Deflater( 3, true ) ) )
                {
                    deflateStream.Write( uncompressedBuffer, 0, uncompressedBuffer.Length );
                }
                return ms.ToArray();
            }
        }

    }
}
