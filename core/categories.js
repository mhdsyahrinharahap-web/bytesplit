const fs = require('node:fs/promises');
const path = require('node:path');

const CATEGORY_LIST = [
  {
    id: 'compressed',
    label: 'Compressed',
    folderName: 'Compressed',
    extensions: [
      '.zip', '.rar', '.7z', '.tar', '.gz', '.bz2', '.xz',
      '.tgz', '.zst', '.cab', '.arj', '.lzh', '.ace', '.z',
    ],
  },
  {
    id: 'documents',
    label: 'Documents',
    folderName: 'Documents',
    extensions: [
      '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
      '.txt', '.rtf', '.odt', '.ods', '.odp', '.csv', '.md',
      '.epub', '.djvu', '.chm',
    ],
  },
  {
    id: 'music',
    label: 'Music',
    folderName: 'Music',
    extensions: [
      '.mp3', '.wav', '.flac', '.aac', '.ogg', '.m4a', '.wma',
      '.opus', '.aiff', '.mid', '.midi',
    ],
  },
  {
    id: 'programs',
    label: 'Programs',
    folderName: 'Programs',
    extensions: [
      '.exe', '.msi', '.msix', '.apk', '.deb', '.rpm', '.dmg',
      '.pkg', '.appimage', '.iso', '.img', '.bin', '.bat',
      '.cmd', '.ps1', '.sh',
    ],
  },
  {
    id: 'video',
    label: 'Video',
    folderName: 'Video',
    extensions: [
      '.mp4', '.mkv', '.avi', '.mov', '.wmv', '.flv', '.webm',
      '.m4v', '.mpeg', '.mpg', '.3gp', '.ts',
    ],
  },
];

const GENERAL_CATEGORY = {
  id: 'general',
  label: 'Lainnya',
  folderName: null,
};

const EXT_TO_CATEGORY = new Map();
const ID_TO_CATEGORY = new Map();

for (const cat of CATEGORY_LIST) {
  ID_TO_CATEGORY.set(cat.id, cat);
  for (const ext of cat.extensions) {
    EXT_TO_CATEGORY.set(ext, cat);
  }
}
ID_TO_CATEGORY.set(GENERAL_CATEGORY.id, GENERAL_CATEGORY);

function detectCategory(fileName) {
  if (!fileName || typeof fileName !== 'string') return GENERAL_CATEGORY;
  const ext = path.extname(fileName.trim()).toLowerCase();
  return EXT_TO_CATEGORY.get(ext) || GENERAL_CATEGORY;
}

function getCategoryById(id) {
  return ID_TO_CATEGORY.get(id) || GENERAL_CATEGORY;
}

function getCategoryDir(baseDir, categoryId) {
  const cat = getCategoryById(categoryId);
  if (!cat.folderName) return baseDir;
  return path.join(baseDir, cat.folderName);
}

async function ensureCategoryFolders(baseDir) {
  await fs.mkdir(baseDir, { recursive: true });
  await Promise.all(
    CATEGORY_LIST.map((cat) => fs.mkdir(path.join(baseDir, cat.folderName), { recursive: true }))
  );
}

module.exports = {
  CATEGORY_LIST,
  GENERAL_CATEGORY,
  detectCategory,
  getCategoryById,
  getCategoryDir,
  ensureCategoryFolders,
};