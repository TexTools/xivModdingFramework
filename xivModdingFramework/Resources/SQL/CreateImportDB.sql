-- Meta table for storing any meta level information not already obviously handled by the table structure.
-- Ex. Author, path to original file, etc.
CREATE TABLE "meta" (
	"key" TEXT NOT NULL UNIQUE,
	"value" TEXT,
	PRIMARY KEY("key")
);

-- Warnings that should be proc'd by TexTools.
CREATE TABLE "warnings" (
	"text" TEXT NOT NULL
);

-- The Triangle Indices
CREATE TABLE "indices" (
	"mesh"	INTEGER NOT NULL,
	"part"   INTEGER NOT NULL,
	"index_id"	INTEGER NOT NULL,
	"vertex_id"	  INTEGER NOT NULL,
	

	PRIMARY KEY("mesh","part","index_id")
);


-- Vertex Data
CREATE TABLE "vertices" (
	"mesh"	INTEGER NOT NULL,
	"part"   INTEGER NOT NULL,
	"vertex_id"	INTEGER NOT NULL,

	-- Position
	"position_x"	REAL NOT NULL,
	"position_y"	REAL NOT NULL,
	"position_z"	REAL NOT NULL,

	-- Normal
	"normal_x"	REAL NOT NULL,
	"normal_y"  REAL NOT NULL,
	"normal_z"	REAL NOT NULL,
	
	-- Vertex Color
	"color_r"	REAL NOT NULL,
	"color_g"	REAL NOT NULL,
	"color_b"	REAL NOT NULL,
	"color_a"	REAL NOT NULL,
	
	-- Vertex Color 2
	"color2_r"	REAL NOT NULL,
	"color2_g"	REAL NOT NULL,
	"color2_b"	REAL NOT NULL,
	"color2_a"	REAL NOT NULL,

	-- UV Coordinates
	"uv_1_u"	REAL NOT NULL,
	"uv_1_v"	REAL NOT NULL,
	"uv_2_u"	REAL NOT NULL,
	"uv_2_v"	REAL NOT NULL,

	-- Bone Weights
	"bone_1_id"			INTEGER,
	"bone_1_weight"		REAL,
	"bone_2_id"			INTEGER,
	"bone_2_weight"		REAL,
	"bone_3_id"			INTEGER,
	"bone_3_weight"		REAL,
	"bone_4_id"			INTEGER,
	"bone_4_weight"		REAL,
	"bone_5_id"			INTEGER,
	"bone_5_weight"		REAL,
	"bone_6_id"			INTEGER,
	"bone_6_weight"		REAL,
	"bone_7_id"			INTEGER,
	"bone_7_weight"		REAL,
	"bone_8_id"			INTEGER,
	"bone_8_weight"		REAL,

	PRIMARY KEY("mesh","part","vertex_id")
);

CREATE TABLE "shape_vertices" (
	"shape" TEXT NOT NULL,
	"mesh" INTEGER NOT NULL,
	"part" INTEGER NOT NULL,
	"vertex_id" INTEGER NOT NULL,
	
	-- Position
	"position_x"	REAL NOT NULL,
	"position_y"	REAL NOT NULL,
	"position_z"	REAL NOT NULL,

	PRIMARY KEY("shape", "mesh", "part", "vertex_id")
);

-- Models
CREATE TABLE "models" (
	"model"	INTEGER NOT NULL,
	"name"   TEXT,

	Primary KEY("model")
);

-- Meshes
CREATE TABLE "meshes" (
	"mesh"	INTEGER NOT NULL,
	"model" INTEGER NOT NULL,
	"material_id" INTEGER,
	"name"   TEXT,
	"type"   TEXT,

	Primary KEY("mesh")
);


-- Parts
CREATE TABLE "parts" (
	"mesh"	INTEGER NOT NULL,
	"part"   INTEGER NOT NULL,
	"name"   TEXT,
	"attributes" TEXT,

	Primary KEY("mesh", "part")
);

-- Bones
CREATE TABLE "bones" (
	"mesh"		INTEGER NOT NULL,
	"bone_id"	INTEGER NOT NULL,
	"name"  TEXT NOT NULL,

	Primary KEY("mesh","bone_id")
);

-- The actual skeleton/bind pose.
CREATE TABLE "skeleton" (
	"name"  TEXT NOT NULL,
	"parent" TEXT,
	
	"matrix_0"	REAL,
	"matrix_1"	REAL,
	"matrix_2"	REAL,
	"matrix_3"	REAL,
	"matrix_4"	REAL,
	"matrix_5"	REAL,
	"matrix_6"	REAL,
	"matrix_7"	REAL,
	"matrix_8"	REAL,
	"matrix_9"	REAL,
	"matrix_10"	REAL,
	"matrix_11"	REAL,
	"matrix_12"	REAL,
	"matrix_13"	REAL,
	"matrix_14"	REAL,
	"matrix_15"	REAL,

	Primary KEY("name")
);

-- Materials
CREATE TABLE "materials" (
	"material_id"	INTEGER NOT NULL,
	"name"			TEXT,
	"diffuse"		TEXT,
	"normal"		TEXT,
	"specular"		TEXT,
	"opacity"		TEXT,
	"emissive"		TEXT,

	Primary KEY("material_id")
);