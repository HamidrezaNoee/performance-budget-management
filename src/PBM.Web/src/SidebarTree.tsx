import { useEffect, useState, type ReactNode } from 'react'
import { Box, Collapse, List, ListItemButton, ListItemIcon, ListItemText } from '@mui/material'
import ExpandLessRoundedIcon from '@mui/icons-material/ExpandLessRounded'
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded'

export type SidebarTreeNode = {
  label: string
  path: string
  children?: SidebarTreeNode[]
}

function Node({ node, depth, selectedPath, onSelect }: { node: SidebarTreeNode; depth: number; selectedPath: string; onSelect: (path: string) => void }) {
  const selected = selectedPath === node.path
  const containsSelected = !!selectedPath && (selected || selectedPath.startsWith(`${node.path}/`))
  const [open, setOpen] = useState(containsSelected)
  const hasChildren = !!node.children?.length

  useEffect(() => { if (containsSelected) setOpen(true) }, [containsSelected])

  return <>
    <ListItemButton
      selected={selected}
      onClick={() => hasChildren ? setOpen(x => !x) : onSelect(node.path)}
      sx={{
        borderRadius: 2,
        mb: .2,
        py: depth <= 1 ? .62 : .48,
        pr: 1.5 + depth * 1.35,
        minHeight: depth <= 1 ? 36 : 31,
        '&.Mui-selected': { bgcolor: 'rgba(56,139,253,.22)' },
        '&:hover': { bgcolor: 'rgba(255,255,255,.055)' }
      }}
    >
      <ListItemIcon sx={{ color: 'inherit', minWidth: 24 }}>
        <Box sx={{ width: depth <= 1 ? 7 : 5, height: depth <= 1 ? 7 : 5, borderRadius: '50%', bgcolor: containsSelected ? '#8ec5ff' : 'rgba(220,232,247,.42)' }} />
      </ListItemIcon>
      <ListItemText
        primary={node.label}
        primaryTypographyProps={{ fontSize: depth <= 1 ? 13.2 : 12.4, fontWeight: hasChildren || selected ? 800 : 550, lineHeight: 1.35 }}
      />
      {hasChildren && (open ? <ExpandLessRoundedIcon sx={{ fontSize: 18 }} /> : <ExpandMoreRoundedIcon sx={{ fontSize: 18 }} />)}
    </ListItemButton>
    {hasChildren && <Collapse in={open} timeout="auto" unmountOnExit>
      <List disablePadding>{node.children!.map(child => <Node key={child.path} node={child} depth={depth + 1} selectedPath={selectedPath} onSelect={onSelect} />)}</List>
    </Collapse>}
  </>
}

export default function SidebarTree({ title, icon, selectedPath, nodes, open, onToggle, onSelect }: {
  title: string
  icon: ReactNode
  selectedPath: string
  nodes: SidebarTreeNode[]
  open: boolean
  onToggle: () => void
  onSelect: (path: string) => void
}) {
  return <>
    <ListItemButton selected={!!selectedPath} onClick={onToggle} sx={{ borderRadius: 2, mt: .5, mb: .35, '&.Mui-selected': { bgcolor: 'rgba(56,139,253,.12)' } }}>
      <ListItemIcon sx={{ color: 'inherit', minWidth: 40 }}>{icon}</ListItemIcon>
      <ListItemText primary={title} primaryTypographyProps={{ fontWeight: 900 }} />
      {open ? <ExpandLessRoundedIcon /> : <ExpandMoreRoundedIcon />}
    </ListItemButton>
    <Collapse in={open} timeout="auto" unmountOnExit>
      <List disablePadding>{nodes.map(node => <Node key={node.path} node={node} depth={1} selectedPath={selectedPath} onSelect={onSelect} />)}</List>
    </Collapse>
  </>
}
