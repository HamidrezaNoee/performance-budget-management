import { Alert, Card, CardContent, Chip, Stack, Typography } from '@mui/material'
import ConstructionRoundedIcon from '@mui/icons-material/ConstructionRounded'

export default function CatalogPlaceholder({ title, description, fields = [] }: { title: string; description?: string; fields?: string[] }) {
  return <Stack spacing={2.5}>
    <Alert severity="info" icon={<ConstructionRoundedIcon />}>
      این صفحه در ساختار سامانه ایجاد شده است. فرم، مدل داده و API آن در فاز تکمیل همین موجودیت اضافه می‌شود.
    </Alert>
    <Card elevation={0}>
      <CardContent>
        <Typography variant="h6" fontWeight={900}>{title}</Typography>
        {description && <Typography color="text.secondary" mt={1}>{description}</Typography>}
        {!!fields.length && <Stack direction="row" flexWrap="wrap" gap={1} mt={2}>
          {fields.map(field => <Chip key={field} label={field} variant="outlined" />)}
        </Stack>}
      </CardContent>
    </Card>
  </Stack>
}
